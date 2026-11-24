using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Ledger.API.Tests
{
    public class PasswordResetIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
    {
        private const string OldPassword = "OldPass123!";

        private async Task<string> RegisterAsync()
        {
            var email = $"{Guid.NewGuid():N}@test.local";
            var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/Register", new { email, password = OldPassword });
            response.EnsureSuccessStatusCode();
            return email;
        }

        private async Task<string> RequestTokenAsync(HttpClient client, string email)
        {
            var response = await client.PostAsJsonAsync("/api/Auth/ForgotPassword", new { email });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return CapturingEmailSender.ExtractToken(Assert.Single(factory.Emails.SentTo(email)));
        }

        [Fact]
        public async Task ForgotPassword_GivesTheSameAnswer_ForKnownAndUnknownEmails()
        {
            var client = factory.CreateClient();
            var known = await RegisterAsync();

            var forKnown = await client.PostAsJsonAsync("/api/Auth/ForgotPassword", new { email = known });
            var forUnknown = await client.PostAsJsonAsync("/api/Auth/ForgotPassword", new { email = $"{Guid.NewGuid():N}@test.local" });

            Assert.Equal(HttpStatusCode.OK, forKnown.StatusCode);
            Assert.Equal(forKnown.StatusCode, forUnknown.StatusCode);
            Assert.Equal(await forKnown.Content.ReadAsStringAsync(), await forUnknown.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ForgotPassword_WithAnInvalidEmailAddress_IsRejected_AndSendsNothing()
        {
            var before = factory.Emails.Sent.Count;

            var response = await factory.CreateClient().PostAsJsonAsync("/api/Auth/ForgotPassword", new { email = "not-an-email" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, factory.Emails.Sent.Count);
        }

        [Fact]
        public async Task EmailedLink_ResetsThePassword_EndToEnd_AndCannotBeReused()
        {
            var client = factory.CreateClient();
            var email = await RegisterAsync();
            var token = await RequestTokenAsync(client, email);

            var reset = await client.PostAsJsonAsync("/api/Auth/ResetPassword", new { token, newPassword = "BrandNew456!" });

            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/Auth/Login", new { email, password = OldPassword })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/Login", new { email, password = "BrandNew456!" })).StatusCode);

            var replay = await client.PostAsJsonAsync("/api/Auth/ResetPassword", new { token, newPassword = "Hijacked789!" });
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/Auth/Login", new { email, password = "Hijacked789!" })).StatusCode);
        }

        [Fact]
        public async Task ResetPassword_WithAGarbageToken_Is400_AndChangesNothing()
        {
            var client = factory.CreateClient();
            var email = await RegisterAsync();

            var response = await client.PostAsJsonAsync("/api/Auth/ResetPassword", new { token = "garbage", newPassword = "BrandNew456!" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/Login", new { email, password = OldPassword })).StatusCode);
        }

        [Fact]
        public async Task ResetPassword_WithATooShortPassword_Is400_AndDoesNotUseUpTheLink()
        {
            var client = factory.CreateClient();
            var email = await RegisterAsync();
            var token = await RequestTokenAsync(client, email);

            var tooShort = await client.PostAsJsonAsync("/api/Auth/ResetPassword", new { token, newPassword = "short" });
            Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

            var retry = await client.PostAsJsonAsync("/api/Auth/ResetPassword", new { token, newPassword = "BrandNew456!" });
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        }

        [Fact]
        public async Task ResetLink_ForOneAccount_CannotResetAnotherAccount()
        {
            var client = factory.CreateClient();
            var alice = await RegisterAsync();
            var bob = await RegisterAsync();
            var aliceToken = await RequestTokenAsync(client, alice);

            await client.PostAsJsonAsync("/api/Auth/ResetPassword", new { token = aliceToken, newPassword = "BrandNew456!" });

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/Login", new { email = bob, password = OldPassword })).StatusCode);
        }
    }
}
