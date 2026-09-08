using Ledger.API.Data;
using Ledger.API.Dtos;
using Ledger.API.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ledger.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowConfiguredOrigins")]
    public class AuthController(
        AppDbContext context,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher) : ControllerBase
    {
        [HttpPost("Register")]
        public IActionResult Register([FromBody] PostUserDto dto)
        {
            if (context.Users.Any(u => u.Email == dto.Email))
            {
                return BadRequest(new { message = "This email address is already taken" });
            }

            var hashedPassword = passwordHasher.HashPassword(null!, dto.Password);

            var newUser = new User()
            {
                Email = dto.Email,
                Password = hashedPassword,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Users.Add(newUser);
            context.SaveChanges();

            var token = GenerateJwtToken(newUser);

            return Ok(new { token, email = newUser.Email });
        }

        [HttpPost("Login")]
        public IActionResult Login([FromBody] LoginUserDto dto)
        {
            var user = context.Users.FirstOrDefault(u => u.Email == dto.Email);
            if (user == null)
            {
                return Unauthorized(new { message = "Invalid credentials" });
            }

            PasswordVerificationResult result;
            try
            {
                result = passwordHasher.VerifyHashedPassword(user, user.Password, dto.Password);
            }
            catch (FormatException)
            {
                // Stored password isn't a valid hash (e.g. a legacy plain-text row) -
                // treat it the same as a failed match rather than a server error.
                result = PasswordVerificationResult.Failed;
            }

            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var token = GenerateJwtToken(user);

            return Ok(new { token, email = user.Email });
        }

        private string GenerateJwtToken(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
