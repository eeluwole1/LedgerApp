namespace Ledger.API.Dtos
{
    public class BudgetProgressDto
    {
        public int BudgetId { get; set; }
        public required string Category { get; set; }
        public decimal MonthlyLimit { get; set; }
        public decimal Spent { get; set; }
        public decimal Remaining { get; set; }
        public decimal PercentUsed { get; set; }
    }
}
