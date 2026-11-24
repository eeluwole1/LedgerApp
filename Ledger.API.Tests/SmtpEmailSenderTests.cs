using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Ledger.API.Mail;
using Xunit;

namespace Ledger.API.Tests
{
    // Talks real SMTP over a loopback socket to a minimal in-process server, so the sender's output
    // (headers, recipient, body) is checked on the wire instead of assumed.
    public class SmtpEmailSenderTests
    {
        private sealed class FakeSmtpServer : IDisposable
        {
            private readonly TcpListener listener = new(IPAddress.Loopback, 0);
            public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
            public string? MailFrom { get; private set; }
            public List<string> Recipients { get; } = [];
            public string Data { get; private set; } = "";
            public Task Session { get; private set; } = Task.CompletedTask;

            public FakeSmtpServer()
            {
                listener.Start();
                Session = Task.Run(RunAsync);
            }

            private async Task RunAsync()
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

                await writer.WriteLineAsync("220 localhost ESMTP ready");
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                        await writer.WriteLineAsync("250 localhost");
                    else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase))
                    {
                        MailFrom = line;
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                    {
                        Recipients.Add(line);
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            data.AppendLine(dataLine);
                        Data = data.ToString();
                        await writer.WriteLineAsync("250 OK queued");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 bye");
                        return;
                    }
                    else
                        await writer.WriteLineAsync("250 OK");
                }
            }

            public void Dispose() => listener.Stop();
        }

        // SmtpClient may encode the body as quoted-printable (soft line breaks, =3D for '=').
        private static string DecodeQuotedPrintable(string text) =>
            Regex.Replace(text.Replace("=\r\n", "").Replace("=\n", ""), "=([0-9A-F]{2})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

        [Fact]
        public async Task Send_DeliversTheMessageOverRealSmtp_WithSenderRecipientSubjectAndIntactLink()
        {
            using var server = new FakeSmtpServer();
            var sender = new SmtpEmailSender(new EmailOptions
            {
                Host = "127.0.0.1",
                Port = server.Port,
                EnableSsl = false,
                From = "no-reply@ledger.test",
                FromName = "LedgerApp"
            });
            var link = "https://app.example.test/reset-password#token=abc-DEF_123xyz";

            await sender.SendAsync(new EmailMessage(
                "someone@test.local",
                "Reset your LedgerApp password",
                $"Choose a new password here:\n{link}\n\nIf you didn't ask for this, ignore it."));
            await server.Session.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("no-reply@ledger.test", server.MailFrom);
            Assert.Contains("someone@test.local", Assert.Single(server.Recipients));
            Assert.Contains("Subject: Reset your LedgerApp password", server.Data);
            Assert.Contains("someone@test.local", server.Data);
            Assert.Contains("LedgerApp", server.Data);
            Assert.Contains(link, DecodeQuotedPrintable(server.Data));
        }

        [Fact]
        public async Task Send_ThrowsWhenTheServerIsUnreachable_SoTheCallerCanHandleIt()
        {
            var free = new TcpListener(IPAddress.Loopback, 0);
            free.Start();
            var closedPort = ((IPEndPoint)free.LocalEndpoint).Port;
            free.Stop();

            var sender = new SmtpEmailSender(new EmailOptions
            {
                Host = "127.0.0.1",
                Port = closedPort,
                EnableSsl = false,
                From = "no-reply@ledger.test"
            });

            await Assert.ThrowsAnyAsync<Exception>(() =>
                sender.SendAsync(new EmailMessage("someone@test.local", "Subject", "Body")));
        }
    }
}
