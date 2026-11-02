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
    public class TransactionsController(ITransactionsService transactionsService) : ControllerBase
    {
        [HttpGet("All")]
        public IActionResult GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] int? year = null, [FromQuery] int? month = null)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            if (!IsValidMonthFilter(year, month))
                return BadRequest("year and month must be provided together, with month 1-12 and a four-digit year");

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;

            var result = transactionsService.GetAll(userId, page, pageSize, year, month);
            return Ok(result);
        }

        [HttpGet("Summary")]
        public IActionResult GetSummary([FromQuery] int? year = null, [FromQuery] int? month = null)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            if (!IsValidMonthFilter(year, month))
                return BadRequest("year and month must be provided together, with month 1-12 and a four-digit year");

            var summary = transactionsService.GetSummary(userId, year, month);
            return Ok(summary);
        }

        [HttpGet("Details/{id}")]
        public IActionResult Get(int id)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            var transactionDb = transactionsService.GetById(id, userId);
            if (transactionDb == null)
                return NotFound();

            return Ok(transactionDb);
        }

        [HttpPost("Create")]
        public IActionResult CreateTransaction([FromBody] PostTransactionDto payload)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            var newTransaction = transactionsService.Add(payload, userId);

            return Ok(newTransaction);
        }

        [HttpPut("Update/{id}")]
        public IActionResult UpdateTransaction(int id, [FromBody] PutTransactionDto payload)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            var updatedTransaction = transactionsService.Update(id, payload, userId);
            if (updatedTransaction == null)
                return NotFound();

            return Ok(updatedTransaction);
        }

        [HttpDelete("Delete/{id}")]
        public IActionResult DeleteTransaction(int id)
        {
            if (!TryGetUserId(out var userId))
                return BadRequest("Could not get the user ID");

            var deleted = transactionsService.Delete(id, userId);
            if (!deleted)
                return NotFound();

            return Ok();
        }

        private static bool IsValidMonthFilter(int? year, int? month)
        {
            if (!year.HasValue && !month.HasValue)
                return true;

            return year.HasValue && month.HasValue
                && year.Value is >= 1000 and <= 9998
                && month.Value is >= 1 and <= 12;
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
