using System.Security.Cryptography;
using System.Text;
using Ledger.API.Mail;
using Ledger.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Ledger.API.Data.Services
{
    public interface IPasswordResetService
    {
        // Never reveals whether the email belongs to an account: it simply returns.
        Task RequestResetAsync(string email);

        // True when the token was valid (known, unexpired, unused) and the password was changed.
        Task<bool> ResetPasswordAsync(string token, string newPassword);
    }

    public class PasswordResetService(
        AppDbContext context,
        IPasswordHasher<User> passwordHasher,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<PasswordResetService> logger) : IPasswordResetService
    {
        public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

        // Stops someone from using the form to flood a victim's inbox.
        public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

        public async Task RequestResetAsync(string email)
        {
            var normalized = email.Trim().ToLowerInvariant();
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == normalized);
            if (user == null)
                return;

            var now = DateTime.UtcNow;
            var issuedAt = user.PasswordResetTokenExpiry - TokenLifetime;
            if (issuedAt > now - ResendCooldown)
                return;

            // 256 random bits. Only the hash is stored, so a leaked database can't be used to reset anyone.
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            user.PasswordResetTokenHash = HashToken(token);
            user.PasswordResetTokenExpiry = now + TokenLifetime;
            user.UpdatedAt = now;
            await context.SaveChangesAsync();

            try
            {
                await emailSender.SendAsync(BuildMessage(user.Email, token));
            }
            catch (Exception ex)
            {
                // Keep the response identical to the success case, but don't leave a token nobody received
                // sitting in the cooldown window where it would block an immediate retry.
                logger.LogError(ex, "Could not send a password reset email.");
                user.PasswordResetTokenHash = null;
                user.PasswordResetTokenExpiry = null;
                await context.SaveChangesAsync();
            }
        }

        public async Task<bool> ResetPasswordAsync(string token, string newPassword)
        {
            var hash = HashToken(token.Trim());
            var now = DateTime.UtcNow;

            var user = await context.Users.FirstOrDefaultAsync(u =>
                u.PasswordResetTokenHash == hash && u.PasswordResetTokenExpiry > now);
            if (user == null)
                return false;

            user.Password = passwordHasher.HashPassword(user, newPassword);
            // Single use: the same link can't be replayed.
            user.PasswordResetTokenHash = null;
            user.PasswordResetTokenExpiry = null;
            user.UpdatedAt = now;
            await context.SaveChangesAsync();

            return true;
        }

        private EmailMessage BuildMessage(string to, string token)
        {
            var baseUrl = configuration["Frontend:BaseUrl"]
                ?? configuration.GetSection("AllowedOrigins").Get<string[]>()?.FirstOrDefault()
                ?? "http://localhost:4200";

            // The token goes in the URL fragment: browsers never send it to a server or in a Referer header.
            var link = $"{baseUrl.TrimEnd('/')}/reset-password#token={token}";

            var body =
                "We received a request to reset your LedgerApp password.\n\n" +
                $"Choose a new password here (the link works once and expires in {(int)TokenLifetime.TotalMinutes} minutes):\n" +
                $"{link}\n\n" +
                "If you didn't ask for this, you can ignore this email. Your password won't change.";

            return new EmailMessage(to, "Reset your LedgerApp password", body);
        }

        private static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
