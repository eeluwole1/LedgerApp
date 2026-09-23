using Ledger.API.Dtos;
using Ledger.API.Models;

namespace Ledger.API.Data.Services
{
    public interface ITransactionsService
    {
        PagedResult<Transaction> GetAll(int userId, int page, int pageSize);
        TransactionSummaryDto GetSummary(int userId);
        Transaction? GetById(int id, int userId);
        Transaction Add(PostTransactionDto transaction, int userId);
        Transaction? Update(int id, PutTransactionDto transaction, int userId);
        bool Delete(int id, int userId);
    }
    public class TransactionsService(AppDbContext context) : ITransactionsService
    {
        public Transaction Add(PostTransactionDto transaction, int userId)
        {
            var newTransaction = new Transaction()
            {
                Amount = transaction.Amount,
                Type = transaction.Type,
                Category = transaction.Category,
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                UserId = userId
            };
            context.Transactions.Add(newTransaction);
            context.SaveChanges();

            return newTransaction;
        }

        public bool Delete(int id, int userId)
        {
            var transactionDb = context.Transactions.FirstOrDefault(t => t.Id == id && t.UserId == userId);
            if (transactionDb == null)
                return false;

            context.Transactions.Remove(transactionDb);
            context.SaveChanges();
            return true;
        }

        public PagedResult<Transaction> GetAll(int userId, int page, int pageSize)
        {
            var query = context.Transactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt);

            var totalCount = query.Count();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new PagedResult<Transaction>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public TransactionSummaryDto GetSummary(int userId)
        {
            var userTransactions = context.Transactions.Where(t => t.UserId == userId);

            var totalIncome = userTransactions.Where(t => t.Type == "Income").Sum(t => (decimal?)t.Amount) ?? 0;
            var totalExpenses = userTransactions.Where(t => t.Type == "Expense").Sum(t => (decimal?)t.Amount) ?? 0;

            return new TransactionSummaryDto
            {
                TotalIncome = totalIncome,
                TotalExpenses = totalExpenses,
                NetBalance = totalIncome - totalExpenses
            };
        }

        public Transaction? GetById(int id, int userId)
        {
            var transactionDb = context.Transactions.FirstOrDefault(t => t.Id == id && t.UserId == userId);
            return transactionDb;
        }

        public Transaction? Update(int id, PutTransactionDto transaction, int userId)
        {
            var transactionDb = context.Transactions.FirstOrDefault(t => t.Id == id && t.UserId == userId);
            if (transactionDb != null)
            {
                transactionDb.Type = transaction.Type;
                transactionDb.Amount = transaction.Amount;
                transactionDb.Category = transaction.Category;
                transactionDb.UpdatedAt = DateTime.UtcNow;

                context.Transactions.Update(transactionDb);
                context.SaveChanges();
            }
            return transactionDb;
        }
    }
}
