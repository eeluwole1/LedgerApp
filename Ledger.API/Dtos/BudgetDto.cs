namespace Ledger.API.Dtos
{
    public class BudgetDto
    {
        public int Id { get; set; }
        public required string Category { get; set; }
        public decimal MonthlyLimit { get; set; }
    }
}
