using System.Net;
using System.Net.Mail;

namespace Ledger.API.Mail
{
    public class SmtpEmailSender(EmailOptions options) : IEmailSender
    {
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(options.From!, options.FromName),
                Subject = message.Subject,
                Body = message.TextBody,
                IsBodyHtml = false
            };
            mail.To.Add(message.To);

            // Short timeout: a slow mail server must not hold the request open for minutes.
            using var client = new SmtpClient(options.Host!, options.Port)
            {
                EnableSsl = options.EnableSsl,
                Timeout = 15_000
            };
            if (!string.IsNullOrEmpty(options.Username))
                client.Credentials = new NetworkCredential(options.Username, options.Password);

            await client.SendMailAsync(mail, cancellationToken);
        }
    }
}
