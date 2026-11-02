using System.ComponentModel.DataAnnotations;

namespace Ledger.API.Dtos
{
    public class UpdateBudgetDto
    {
        [Range(typeof(decimal), "0.01", "999999999999999")]
        public decimal MonthlyLimit { get; set; }
    }
}
