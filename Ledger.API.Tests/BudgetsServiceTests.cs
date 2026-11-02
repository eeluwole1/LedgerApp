using Ledger.API.Data;
using Ledger.API.Data.Services;
using Ledger.API.Dtos;
using Ledger.API.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ledger.API.Tests
{
    public class BudgetsServiceTests
    {
        private static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static Transaction Tx(int userId, string type, string category, decimal amount, DateTime date) => new()
        {
            Type = type,
            Category = category,
            Amount = amount,
            UserId = userId,
            CreatedAt = date,
            UpdatedAt = date
        };

        [Fact]
        public void Add_CreatesBudgetForUser()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);

            var created = service.Add(new SaveBudgetDto { Category = "  Food ", MonthlyLimit = 400m }, 1);

            Assert.NotNull(created);
            Assert.Equal("Food", created!.Category);
            Assert.Equal(400m, created.MonthlyLimit);
            Assert.Single(service.GetAll(1));
        }

        [Fact]
        public void Add_SecondBudgetForSameCategory_ReturnsNull()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 400m }, 1);

            var duplicate = service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 999m }, 1);

            Assert.Null(duplicate);
            Assert.Equal(400m, Assert.Single(service.GetAll(1)).MonthlyLimit);
        }

        [Fact]
        public void Add_SameCategoryForDifferentUsers_IsAllowed()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);

            Assert.NotNull(service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 400m }, 1));
            Assert.NotNull(service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 100m }, 2));
        }

        [Fact]
        public void GetAll_NeverReturnsAnotherUsersBudgets()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 400m }, 1);
            service.Add(new SaveBudgetDto { Category = "Rent", MonthlyLimit = 1200m }, 2);

            var mine = service.GetAll(1);

            Assert.Equal("Food", Assert.Single(mine).Category);
        }

        [Fact]
        public void UpdateAndDelete_OnSomeoneElsesBudget_BehaveLikeItDoesNotExist()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            var theirs = service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 400m }, 2)!;

            Assert.Null(service.Update(theirs.Id, new UpdateBudgetDto { MonthlyLimit = 1m }, 1));
            Assert.False(service.Delete(theirs.Id, 1));
            Assert.Equal(400m, Assert.Single(service.GetAll(2)).MonthlyLimit);
        }

        [Fact]
        public void Update_ChangesLimit_AndDelete_RemovesIt()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            var created = service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 400m }, 1)!;

            var updated = service.Update(created.Id, new UpdateBudgetDto { MonthlyLimit = 450.50m }, 1);

            Assert.Equal(450.50m, updated!.MonthlyLimit);
            Assert.True(service.Delete(created.Id, 1));
            Assert.Empty(service.GetAll(1));
        }

        [Fact]
        public void GetProgress_SumsOnlyThatUsersExpensesInThatCategoryAndMonth()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 100m }, 1);

            context.Transactions.AddRange(
                Tx(1, "Expense", "Food", 10.10m, new DateTime(2026, 9, 1)),
                Tx(1, "Expense", "Food", 20.20m, new DateTime(2026, 9, 30)),
                Tx(1, "Expense", "Food", 99m, new DateTime(2026, 8, 31)),
                Tx(1, "Expense", "Food", 99m, new DateTime(2026, 10, 1)),
                Tx(1, "Expense", "Transportation", 99m, new DateTime(2026, 9, 10)),
                Tx(1, "Income", "Food", 99m, new DateTime(2026, 9, 10)),
                Tx(2, "Expense", "Food", 99m, new DateTime(2026, 9, 10))
            );
            context.SaveChanges();

            var progress = Assert.Single(service.GetProgress(1, 2026, 9));

            Assert.Equal(30.30m, progress.Spent);
            Assert.Equal(69.70m, progress.Remaining);
            Assert.Equal(30.3m, progress.PercentUsed);
        }

        [Fact]
        public void GetProgress_WhenOverBudget_RemainingIsNegativeAndPercentExceedsHundred()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 100m }, 1);
            context.Transactions.Add(Tx(1, "Expense", "Food", 150m, new DateTime(2026, 9, 5)));
            context.SaveChanges();

            var progress = Assert.Single(service.GetProgress(1, 2026, 9));

            Assert.Equal(-50m, progress.Remaining);
            Assert.Equal(150m, progress.PercentUsed);
        }

        [Fact]
        public void GetProgress_BudgetWithNoSpending_ShowsZeroSpent()
        {
            using var context = CreateContext();
            var service = new BudgetsService(context);
            service.Add(new SaveBudgetDto { Category = "Food", MonthlyLimit = 100m }, 1);

            var progress = Assert.Single(service.GetProgress(1, 2026, 9));

            Assert.Equal(0m, progress.Spent);
            Assert.Equal(100m, progress.Remaining);
            Assert.Equal(0m, progress.PercentUsed);
        }
    }
}
