namespace Ledger.API.Dtos
{
    public class PostTransactionDto
    {
        public required string Type { get; set; }
        public double Amount { get; set; }
        public required string Category { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
