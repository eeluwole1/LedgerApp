using Ledger.API.Data;
using Ledger.API.Data.Services;
using Ledger.API.Dtos;
using Ledger.API.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ledger.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowConfiguredOrigins")]
    [EnableRateLimiting("auth")]
    public class AuthController(
        AppDbContext context,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher,
        IPasswordResetService passwordResetService) : ControllerBase
    {
        [HttpPost("Register")]
        public IActionResult Register([FromBody] PostUserDto dto)
        {
            var email = dto.Email.Trim().ToLowerInvariant();
            if (context.Users.Any(u => u.Email == email))
            {
                return BadRequest(new { message = "This email address is already taken" });
            }

            var hashedPassword = passwordHasher.HashPassword(null!, dto.Password);

            var newUser = new User()
            {
                Email = email,
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
            var email = dto.Email.Trim().ToLowerInvariant();
            var user = context.Users.FirstOrDefault(u => u.Email == email);
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

        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            await passwordResetService.RequestResetAsync(dto.Email);

            // Same answer whether or not the email has an account, so this can't be used to find out who's registered.
            return Ok(new { message = "If that email is registered, a reset link is on its way." });
        }

        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var changed = await passwordResetService.ResetPasswordAsync(dto.Token, dto.NewPassword);
            if (!changed)
                return BadRequest(new { message = "This reset link is invalid or has expired." });

            return Ok(new { message = "Your password has been updated. You can now sign in." });
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
