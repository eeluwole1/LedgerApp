using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ledger.API.Tests
{
    // Same app, but with a sign-in limit of 3 attempts per window.
    public class RateLimitedApiFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "3" }));
        }
    }

    public class AuthRateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
    {
        [Fact]
        public async Task RepeatedFailedLogins_AreRefusedWith429_OnceTheLimitIsReached()
        {
            var client = factory.CreateClient();
            var attempt = new { email = "someone@test.local", password = "wrong-password" };

            for (var i = 0; i < 3; i++)
            {
                var response = await client.PostAsJsonAsync("/api/Auth/Login", attempt);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            var blocked = await client.PostAsJsonAsync("/api/Auth/Login", attempt);
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        }
    }

    public class AuthValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
    {
        [Theory]
        [InlineData("not-an-email", "TestPass123!")]
        [InlineData("short@test.local", "short")]
        [InlineData("", "TestPass123!")]
        public async Task Register_RejectsInvalidInput_With400(string email, string password)
        {
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/Auth/Register", new { email, password });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Email_IsMatchedCaseInsensitively_AfterSignUp()
        {
            var client = factory.CreateClient();
            var local = Guid.NewGuid().ToString("N");

            var register = await client.PostAsJsonAsync("/api/Auth/Register",
                new { email = $"  {local.ToUpperInvariant()}@Test.Local ", password = "TestPass123!" });
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);

            var login = await client.PostAsJsonAsync("/api/Auth/Login",
                new { email = $"{local}@test.local", password = "TestPass123!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        [Fact]
        public async Task SwaggerUi_IsNotServed_OutsideDevelopment()
        {
            var client = factory.CreateClient();

            var response = await client.GetAsync("/swagger/index.html");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
