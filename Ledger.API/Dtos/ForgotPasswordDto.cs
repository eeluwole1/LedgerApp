using System.ComponentModel.DataAnnotations;

namespace Ledger.API.Dtos
{
    public class ForgotPasswordDto
    {
        [Required, EmailAddress, MaxLength(254)]
        public required string Email { get; set; }
    }
}
