using System.ComponentModel.DataAnnotations;

namespace Ledger.API.Dtos
{
    public class SaveBudgetDto
    {
        [Required, StringLength(50, MinimumLength = 1)]
        public required string Category { get; set; }

        [Range(typeof(decimal), "0.01", "999999999999999")]
        public decimal MonthlyLimit { get; set; }
    }
}
