using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ledger.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.API.Tests
{
    // Boots the real app (routing, JWT auth, controllers, services) against an in-memory database.
    public class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = "integration-" + Guid.NewGuid();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user-secrets (the real Azure SQL connection string).
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                var toRemove = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                                || d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>))
                    .ToList();
                foreach (var descriptor in toRemove)
                    services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        }

        // Registers a brand-new user through the real endpoint and returns a client that sends their JWT.
        public async Task<HttpClient> CreateAuthenticatedClientAsync()
        {
            var client = CreateClient();
            var credentials = new { email = $"{Guid.NewGuid():N}@test.local", password = "TestPass123!" };

            var response = await client.PostAsJsonAsync("/api/Auth/Register", credentials);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<AuthBody>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
            return client;
        }

        public record AuthBody(string Token, string Email);
    }
}
