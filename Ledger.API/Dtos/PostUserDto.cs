using System.ComponentModel.DataAnnotations;

namespace Ledger.API.Dtos
{
    public class PostUserDto
    {
        [Required, EmailAddress, MaxLength(254)]
        public required string Email { get; set; }

        [Required, MinLength(8, ErrorMessage = "Use a password of at least 8 characters."), MaxLength(128)]
        public required string Password { get; set; }
    }
}
