using System.ComponentModel.DataAnnotations;

namespace Ledger.API.Dtos
{
    public class ResetPasswordDto
    {
        [Required, MaxLength(200)]
        public required string Token { get; set; }

        [Required, MinLength(8, ErrorMessage = "Use a password of at least 8 characters."), MaxLength(128)]
        public required string NewPassword { get; set; }
    }
}
