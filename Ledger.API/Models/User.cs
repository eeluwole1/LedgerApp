using System.ComponentModel.DataAnnotations;
using Ledger.API.Models.Base;

namespace Ledger.API.Models
{
    public class User:BaseEntity
    {
        public required string Email { get; set; }
        public required string Password { get; set; }

        // SHA-256 (hex) of the emailed reset token. The token itself is never stored.
        [MaxLength(64)]
        public string? PasswordResetTokenHash { get; set; }
        public DateTime? PasswordResetTokenExpiry { get; set; }

        public List<Transaction> Transactions { get; set; } = [];
    }
}
