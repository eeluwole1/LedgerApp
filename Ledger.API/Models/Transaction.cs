using System.ComponentModel.DataAnnotations.Schema;
using Ledger.API.Models.Base;

namespace Ledger.API.Models
{
    public class Transaction:BaseEntity
    {
        public required string Type { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        public required string Category { get; set; }
        public int? UserId { get; set; }
        public virtual User? User { get; set; }
    }

}