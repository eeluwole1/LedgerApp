namespace Ledger.API.Dtos
{
    public class PutTransactionDto
    {
        public required string Type { get; set; }
        public decimal Amount { get; set; }
        public required string Category { get; set; }
    }
}
