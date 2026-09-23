namespace Ledger.API.Dtos
{
    public class TransactionSummaryDto
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetBalance { get; set; }
    }
}
