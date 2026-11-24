using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Ledger.API.Tests
{
    // Same app, but with a mail server configured and (optionally) a sender address.
    public class MailConfiguredFactory(string? from) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Email:Host", "smtp.example.test");
            if (from is not null)
                builder.UseSetting("Email:From", from);
        }
    }

    public class EmailConfigurationTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not an address")]
        public void Startup_Fails_WhenTheMailHostIsSet_ButTheSenderAddressIsMissingOrInvalid(string? from)
        {
            using var factory = new MailConfiguredFactory(from);

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("Email:From", ex.Message);
        }

        [Fact]
        public void Startup_Succeeds_WithAValidMailHostAndSender()
        {
            using var factory = new MailConfiguredFactory("no-reply@ledger.test");

            using var client = factory.CreateClient();

            Assert.NotNull(client);
        }
    }
}
