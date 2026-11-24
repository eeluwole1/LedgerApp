using Ledger.API.Data;
using Ledger.API.Data.Services;
using Ledger.API.Mail;
using Ledger.API.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net.Mail;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Outside Development and the test host, refuse to start with a missing, placeholder or short
// signing key: anyone who knows the key can forge sign-in tokens.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    var jwtKey = builder.Configuration["Jwt:Key"] ?? "";
    if (Encoding.UTF8.GetByteCount(jwtKey) < 32
        || jwtKey.StartsWith("REPLACE_", StringComparison.Ordinal)
        || jwtKey.StartsWith("your-", StringComparison.Ordinal)
        || jwtKey.StartsWith("local-development-only", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Jwt:Key must be set to a random secret of at least 32 bytes (for example the Jwt__Key app setting in Azure).");
    }
}

// Add services to the container.
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(opt => opt.AddPolicy("AllowConfiguredOrigins",
    policy => policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod())
    );

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

// Sign-in and sign-up are limited per client IP address to slow down password guessing
// (defaults: 10 attempts per 5 minutes; override with RateLimiting__AuthPermitLimit etc.).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
    {
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = config.GetValue("RateLimiting:AuthPermitLimit", 10);
        var windowMinutes = config.GetValue("RateLimiting:AuthWindowMinutes", 5);
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0
            });
    });
});

var conn = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlServer(conn));
builder.Services.AddScoped<ITransactionsService, TransactionsService>();
builder.Services.AddScoped<IBudgetsService, BudgetsService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Password reset emails: real SMTP when Email:Host is set, otherwise nothing is sent (see UnconfiguredEmailSender).
// A half-configured mail section fails at startup rather than on the first user who forgets a password.
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
if (!string.IsNullOrWhiteSpace(emailOptions.Host)
    && !(MailAddress.TryCreate(emailOptions.From, out _)))
{
    throw new InvalidOperationException(
        "Email:Host is set, so Email:From must be a valid sender address (for example the Email__From app setting in Azure).");
}
builder.Services.AddSingleton<IEmailSender>(sp => string.IsNullOrWhiteSpace(emailOptions.Host)
    ? new UnconfiguredEmailSender(
        sp.GetRequiredService<ILogger<UnconfiguredEmailSender>>(),
        sp.GetRequiredService<IHostEnvironment>())
    : new SmtpEmailSender(emailOptions));
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// API documentation is for local development only; it is not served in production.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("AllowConfiguredOrigins");

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();

// Lets the integration tests (WebApplicationFactory<Program>) see the top-level Program class.
public partial class Program { }
