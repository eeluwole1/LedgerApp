namespace Ledger.API.Mail
{
    public record EmailMessage(string To, string Subject, string TextBody);

    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}
