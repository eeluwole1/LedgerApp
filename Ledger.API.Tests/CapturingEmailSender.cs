using System.Collections.Concurrent;
using Ledger.API.Mail;

namespace Ledger.API.Tests
{
    // Records every email instead of sending it, so tests can read the reset link a user would receive.
    public class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> sent = new();

        public bool ThrowOnSend { get; set; }

        public IReadOnlyList<EmailMessage> Sent => sent.ToList();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
                throw new InvalidOperationException("The mail server is down.");

            sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public IReadOnlyList<EmailMessage> SentTo(string address) =>
            Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

        // The reset link carries the token in the URL fragment: https://app/reset-password#token=<token>
        public static string ExtractToken(EmailMessage message)
        {
            const string marker = "#token=";
            var start = message.TextBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = message.TextBody.IndexOfAny(['\r', '\n', ' '], start);
            return message.TextBody[start..(end < 0 ? message.TextBody.Length : end)];
        }
    }
}
