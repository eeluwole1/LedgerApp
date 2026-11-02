using Ledger.API.Data;
using Ledger.API.Data.Services;
using Ledger.API.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ledger.API.Tests
{
    public class TransactionMonthFilterTests
    {
        private static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static Transaction Tx(int userId, string type, decimal amount, DateTime date) => new()
        {
            Type = type,
            Amount = amount,
            Category = "Test",
            UserId = userId,
            CreatedAt = date,
            UpdatedAt = date
        };

        private static AppDbContext SeedBoundaryData()
        {
            var context = CreateContext();
            context.Transactions.AddRange(
                Tx(1, "Expense", 10m, new DateTime(2026, 8, 31)),
                Tx(1, "Expense", 20m, new DateTime(2026, 9, 1)),
                Tx(1, "Income", 100m, new DateTime(2026, 9, 15)),
                Tx(1, "Expense", 30m, new DateTime(2026, 9, 30)),
                Tx(1, "Expense", 40m, new DateTime(2026, 10, 1)),
                Tx(2, "Expense", 999m, new DateTime(2026, 9, 10))
            );
            context.SaveChanges();
            return context;
        }

        [Fact]
        public void GetSummary_WithMonth_IncludesFirstAndLastDayOfMonthButNotNeighbours()
        {
            using var context = SeedBoundaryData();
            var service = new TransactionsService(context);

            var summary = service.GetSummary(1, 2026, 9);

            Assert.Equal(100m, summary.TotalIncome);
            Assert.Equal(50m, summary.TotalExpenses);
            Assert.Equal(50m, summary.NetBalance);
        }

        [Fact]
        public void GetSummary_WithoutMonth_StillCoversAllTimeForThatUser()
        {
            using var context = SeedBoundaryData();
            var service = new TransactionsService(context);

            var summary = service.GetSummary(1);

            Assert.Equal(100m, summary.TotalIncome);
            Assert.Equal(100m, summary.TotalExpenses);
        }

        [Fact]
        public void GetAll_WithMonth_ReturnsOnlyThatMonthsRowsAndCount()
        {
            using var context = SeedBoundaryData();
            var service = new TransactionsService(context);

            var result = service.GetAll(1, 1, 10, 2026, 9);

            Assert.Equal(3, result.TotalCount);
            Assert.All(result.Items, t => Assert.Equal(9, t.CreatedAt.Month));
        }

        [Fact]
        public void GetAll_WithMonth_NeverReturnsAnotherUsersTransactions()
        {
            using var context = SeedBoundaryData();
            var service = new TransactionsService(context);

            var result = service.GetAll(1, 1, 10, 2026, 9);

            Assert.DoesNotContain(result.Items, t => t.Amount == 999m);
        }
    }
}
