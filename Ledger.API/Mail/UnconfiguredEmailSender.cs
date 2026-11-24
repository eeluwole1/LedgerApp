namespace Ledger.API.Mail
{
    // Used when no Email:Host is configured. Never sends anything. In Development it logs the
    // message so you can copy the reset link; elsewhere it only warns, because the message
    // contains a live reset token and must not end up in production logs.
    public class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger, IHostEnvironment environment) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Email is not configured. Development only, this is what would have been sent to {To}:{NewLine}{Subject}{NewLine}{Body}",
                    message.To, Environment.NewLine, message.Subject, Environment.NewLine, message.TextBody);
            }
            else
            {
                logger.LogWarning(
                    "Email is not configured (Email:Host is empty), so \"{Subject}\" was not sent. Set the Email__* settings to enable it.",
                    message.Subject);
            }

            return Task.CompletedTask;
        }
    }
}
