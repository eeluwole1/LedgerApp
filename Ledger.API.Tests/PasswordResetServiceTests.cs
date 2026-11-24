using Ledger.API.Data;
using Ledger.API.Data.Services;
using Ledger.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ledger.API.Tests
{
    public class PasswordResetServiceTests
    {
        private const string OldPassword = "OldPass123!";
        private const string UserEmail = "reset-me@test.local";

        private readonly PasswordHasher<User> hasher = new();
        private readonly CapturingEmailSender emails = new();

        private AppDbContext CreateContextWithUser()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new AppDbContext(options);

            var now = DateTime.UtcNow;
            context.Users.Add(new User
            {
                Email = UserEmail,
                Password = hasher.HashPassword(null!, OldPassword),
                CreatedAt = now,
                UpdatedAt = now
            });
            context.SaveChanges();
            return context;
        }

        private PasswordResetService CreateService(AppDbContext context)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "https://app.example.test/" })
                .Build();
            return new PasswordResetService(context, hasher, emails, config, NullLogger<PasswordResetService>.Instance);
        }

        private bool PasswordIs(AppDbContext context, string password) =>
            hasher.VerifyHashedPassword(null!, context.Users.Single().Password, password) != PasswordVerificationResult.Failed;

        [Fact]
        public async Task UnknownEmail_SendsNothing_AndDoesNotThrow()
        {
            using var context = CreateContextWithUser();

            await CreateService(context).RequestResetAsync("nobody@test.local");

            Assert.Empty(emails.Sent);
        }

        [Fact]
        public async Task KnownEmail_SendsALinkToTheConfiguredFrontend_AndStoresOnlyAHashOfTheToken()
        {
            using var context = CreateContextWithUser();

            await CreateService(context).RequestResetAsync(UserEmail);

            var message = Assert.Single(emails.Sent);
            var token = CapturingEmailSender.ExtractToken(message);
            Assert.Equal(UserEmail, message.To);
            Assert.Contains("https://app.example.test/reset-password#token=", message.TextBody);
            Assert.True(token.Length >= 40);

            var stored = context.Users.Single();
            Assert.NotNull(stored.PasswordResetTokenHash);
            Assert.NotEqual(token, stored.PasswordResetTokenHash);
            Assert.DoesNotContain(token, stored.PasswordResetTokenHash!);
            Assert.Equal(64, stored.PasswordResetTokenHash!.Length);
            Assert.True(stored.PasswordResetTokenExpiry > DateTime.UtcNow.AddMinutes(55));
        }

        [Fact]
        public async Task EmailLookup_IgnoresCaseAndSurroundingSpaces()
        {
            using var context = CreateContextWithUser();

            await CreateService(context).RequestResetAsync("  RESET-ME@Test.Local ");

            Assert.Single(emails.Sent);
        }

        [Fact]
        public async Task SecondRequestInsideTheCooldown_DoesNotSendAnotherEmail()
        {
            using var context = CreateContextWithUser();
            var service = CreateService(context);

            await service.RequestResetAsync(UserEmail);
            await service.RequestResetAsync(UserEmail);

            Assert.Single(emails.Sent);
        }

        [Fact]
        public async Task RequestAfterTheCooldown_IssuesANewLink_AndTheOldOneStopsWorking()
        {
            using var context = CreateContextWithUser();
            var service = CreateService(context);
            await service.RequestResetAsync(UserEmail);
            var oldToken = CapturingEmailSender.ExtractToken(emails.Sent[0]);

            // Pretend that link went out three minutes ago.
            var user = context.Users.Single();
            user.PasswordResetTokenExpiry = DateTime.UtcNow + PasswordResetService.TokenLifetime - TimeSpan.FromMinutes(3);
            context.SaveChanges();

            await service.RequestResetAsync(UserEmail);

            Assert.Equal(2, emails.Sent.Count);
            Assert.False(await service.ResetPasswordAsync(oldToken, "NewPass123!"));
            Assert.True(await service.ResetPasswordAsync(CapturingEmailSender.ExtractToken(emails.Sent[1]), "NewPass123!"));
        }

        [Fact]
        public async Task ValidToken_ChangesThePassword_AndWorksOnlyOnce()
        {
            using var context = CreateContextWithUser();
            var service = CreateService(context);
            await service.RequestResetAsync(UserEmail);
            var token = CapturingEmailSender.ExtractToken(emails.Sent[0]);

            Assert.True(await service.ResetPasswordAsync(token, "NewPass123!"));

            Assert.True(PasswordIs(context, "NewPass123!"));
            Assert.False(PasswordIs(context, OldPassword));
            Assert.Null(context.Users.Single().PasswordResetTokenHash);
            Assert.False(await service.ResetPasswordAsync(token, "AnotherPass123!"));
            Assert.True(PasswordIs(context, "NewPass123!"));
        }

        [Fact]
        public async Task ExpiredToken_IsRejected_AndLeavesThePasswordAlone()
        {
            using var context = CreateContextWithUser();
            var service = CreateService(context);
            await service.RequestResetAsync(UserEmail);
            var token = CapturingEmailSender.ExtractToken(emails.Sent[0]);

            context.Users.Single().PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(-1);
            context.SaveChanges();

            Assert.False(await service.ResetPasswordAsync(token, "NewPass123!"));
            Assert.True(PasswordIs(context, OldPassword));
        }

        [Theory]
        [InlineData("not-a-real-token")]
        [InlineData("")]
        public async Task UnknownToken_IsRejected_AndLeavesThePasswordAlone(string token)
        {
            using var context = CreateContextWithUser();
            var service = CreateService(context);
            await service.RequestResetAsync(UserEmail);

            Assert.False(await service.ResetPasswordAsync(token, "NewPass123!"));
            Assert.True(PasswordIs(context, OldPassword));
        }

        [Fact]
        public async Task WhenTheEmailCannotBeSent_NothingThrows_AndTheTokenIsCleared()
        {
            using var context = CreateContextWithUser();
            emails.ThrowOnSend = true;

            await CreateService(context).RequestResetAsync(UserEmail);

            var user = context.Users.Single();
            Assert.Null(user.PasswordResetTokenHash);
            Assert.Null(user.PasswordResetTokenExpiry);

            // The cooldown didn't swallow the user's retry.
            emails.ThrowOnSend = false;
            await CreateService(context).RequestResetAsync(UserEmail);
            Assert.Single(emails.Sent);
        }
    }
}
