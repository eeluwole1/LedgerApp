using System.ComponentModel.DataAnnotations;

namespace Ledger.API.Dtos
{
    public class LoginUserDto
    {
        [Required, MaxLength(254)]
        public required string Email { get; set; }

        [Required, MaxLength(128)]
        public required string Password { get; set; }
    }
}
