using Ledger.API.Data.Services;
using Ledger.API.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ledger.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowConfiguredOrigins")]
    [Authorize]
    public class BudgetsController(IBudgetsService budgetsService) : ControllerBase
    {
        [HttpGet("All")]
        public IActionResult GetAll()
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            return Ok(budgetsService.GetAll(userId));
        }

        [HttpGet("Progress")]
        public IActionResult GetProgress([FromQuery] int? year = null, [FromQuery] int? month = null)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            if (year.HasValue != month.HasValue
                || (year.HasValue && (year.Value is < 1000 or > 9998 || month!.Value is < 1 or > 12)))
                return BadRequest("year and month must be provided together, with month 1-12 and a four-digit year");

            var now = DateTime.UtcNow;
            return Ok(budgetsService.GetProgress(userId, year ?? now.Year, month ?? now.Month));
        }

        [HttpPost("Create")]
        public IActionResult Create([FromBody] SaveBudgetDto payload)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            var created = budgetsService.Add(payload, userId);
            if (created == null)
                return Conflict(new { message = "You already have a budget for that category." });

            return Ok(created);
        }

        [HttpPut("Update/{id}")]
        public IActionResult Update(int id, [FromBody] UpdateBudgetDto payload)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            var updated = budgetsService.Update(id, payload, userId);
            if (updated == null)
                return NotFound();

            return Ok(updated);
        }

        [HttpDelete("Delete/{id}")]
        public IActionResult Delete(int id)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            if (!budgetsService.Delete(id, userId))
                return NotFound();

            return Ok();
        }

        private bool TryGetUserId(out int userId)
        {
            userId = 0;
            var nameIdentifierClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(nameIdentifierClaim))
                return false;

            return int.TryParse(nameIdentifierClaim, out userId);
        }
    }
}
