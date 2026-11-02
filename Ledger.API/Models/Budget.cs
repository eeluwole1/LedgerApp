using System.ComponentModel.DataAnnotations.Schema;
using Ledger.API.Models.Base;

namespace Ledger.API.Models
{
    public class Budget : BaseEntity
    {
        public required string Category { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal MonthlyLimit { get; set; }
        public int UserId { get; set; }
        public virtual User? User { get; set; }
    }
}
