using Ledger.API.Dtos;
using Ledger.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ledger.API.Data.Services
{
    public interface IBudgetsService
    {
        List<BudgetDto> GetAll(int userId);
        BudgetDto? Add(SaveBudgetDto budget, int userId);
        BudgetDto? Update(int id, UpdateBudgetDto budget, int userId);
        bool Delete(int id, int userId);
        List<BudgetProgressDto> GetProgress(int userId, int year, int month);
    }

    public class BudgetsService(AppDbContext context) : IBudgetsService
    {
        public List<BudgetDto> GetAll(int userId)
        {
            return context.Budgets
                .Where(b => b.UserId == userId)
                .OrderBy(b => b.Category)
                .Select(b => new BudgetDto { Id = b.Id, Category = b.Category, MonthlyLimit = b.MonthlyLimit })
                .ToList();
        }

        // Returns null when this user already has a budget for the category.
        public BudgetDto? Add(SaveBudgetDto budget, int userId)
        {
            var category = budget.Category.Trim();

            if (context.Budgets.Any(b => b.UserId == userId && b.Category == category))
                return null;

            var now = DateTime.UtcNow;
            var entity = new Budget
            {
                Category = category,
                MonthlyLimit = budget.MonthlyLimit,
                UserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.Budgets.Add(entity);
            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException)
            {
                // Lost a race with a concurrent request: the unique (UserId, Category) index rejected it.
                return null;
            }

            return ToDto(entity);
        }

        public BudgetDto? Update(int id, UpdateBudgetDto budget, int userId)
        {
            var entity = context.Budgets.FirstOrDefault(b => b.Id == id && b.UserId == userId);
            if (entity == null)
                return null;

            entity.MonthlyLimit = budget.MonthlyLimit;
            entity.UpdatedAt = DateTime.UtcNow;
            context.SaveChanges();

            return ToDto(entity);
        }

        public bool Delete(int id, int userId)
        {
            var entity = context.Budgets.FirstOrDefault(b => b.Id == id && b.UserId == userId);
            if (entity == null)
                return false;

            context.Budgets.Remove(entity);
            context.SaveChanges();
            return true;
        }

        public List<BudgetProgressDto> GetProgress(int userId, int year, int month)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1);

            var spentRows = context.Transactions
                .Where(t => t.UserId == userId && t.Type == "Expense" && t.CreatedAt >= start && t.CreatedAt < end)
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Spent = g.Sum(t => t.Amount) })
                .ToList();

            var spentByCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in spentRows)
                spentByCategory[row.Category] = spentByCategory.GetValueOrDefault(row.Category) + row.Spent;

            return context.Budgets
                .Where(b => b.UserId == userId)
                .OrderBy(b => b.Category)
                .ToList()
                .Select(b =>
                {
                    var spent = spentByCategory.GetValueOrDefault(b.Category);
                    return new BudgetProgressDto
                    {
                        BudgetId = b.Id,
                        Category = b.Category,
                        MonthlyLimit = b.MonthlyLimit,
                        Spent = spent,
                        Remaining = b.MonthlyLimit - spent,
                        PercentUsed = b.MonthlyLimit > 0 ? Math.Round(spent / b.MonthlyLimit * 100, 1) : 0
                    };
                })
                .ToList();
        }

        private static BudgetDto ToDto(Budget b) =>
            new() { Id = b.Id, Category = b.Category, MonthlyLimit = b.MonthlyLimit };
    }
}
