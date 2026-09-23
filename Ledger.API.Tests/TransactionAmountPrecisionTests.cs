using Ledger.API.Data;
using Ledger.API.Data.Services;
using Ledger.API.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ledger.API.Tests
{
    public class TransactionAmountPrecisionTests
    {
        private static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public void GetSummary_SumsFractionalAmounts_WithExactDecimalPrecision()
        {
            using var context = CreateContext();
            const int userId = 1;
            var now = DateTime.UtcNow;

            // 0.1 + 0.2 is the classic case where binary floating-point (double)
            // rounds to 0.30000000000000004 instead of 0.3. Amount is decimal
            // specifically so a ledger's totals don't drift like that.
            context.Transactions.AddRange(
                new Transaction { Type = "Income", Amount = 0.1m, Category = "Test", UserId = userId, CreatedAt = now, UpdatedAt = now },
                new Transaction { Type = "Income", Amount = 0.2m, Category = "Test", UserId = userId, CreatedAt = now, UpdatedAt = now },
                new Transaction { Type = "Expense", Amount = 0.15m, Category = "Test", UserId = userId, CreatedAt = now, UpdatedAt = now }
            );
            context.SaveChanges();

            var service = new TransactionsService(context);
            var summary = service.GetSummary(userId);

            Assert.Equal(0.3m, summary.TotalIncome);
            Assert.Equal(0.15m, summary.TotalExpenses);
            Assert.Equal(0.15m, summary.NetBalance);
        }

        [Fact]
        public void GetSummary_OnlyIncludesTheRequestingUsersTransactions()
        {
            using var context = CreateContext();
            var now = DateTime.UtcNow;

            context.Transactions.AddRange(
                new Transaction { Type = "Income", Amount = 100m, Category = "Test", UserId = 1, CreatedAt = now, UpdatedAt = now },
                new Transaction { Type = "Income", Amount = 999m, Category = "Test", UserId = 2, CreatedAt = now, UpdatedAt = now }
            );
            context.SaveChanges();

            var service = new TransactionsService(context);
            var summary = service.GetSummary(1);

            Assert.Equal(100m, summary.TotalIncome);
        }
    }
}
