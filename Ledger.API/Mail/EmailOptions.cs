namespace Ledger.API.Mail
{
    // Bound from the "Email" configuration section (Email__Host, Email__Password, ... in Azure).
    // When Host is empty no email is sent; see UnconfiguredEmailSender.
    public class EmailOptions
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? From { get; set; }
        public string FromName { get; set; } = "LedgerApp";
    }
}
