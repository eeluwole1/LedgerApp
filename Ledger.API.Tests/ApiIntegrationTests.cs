using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Ledger.API.Tests
{
    public class ApiIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
    {
        private record TransactionBody(int Id, string Type, decimal Amount, string Category, DateTime CreatedAt);
        private record PagedBody(List<TransactionBody> Items, int TotalCount, int Page, int PageSize);
        private record SummaryBody(decimal TotalIncome, decimal TotalExpenses, decimal NetBalance);
        private record BudgetBody(int Id, string Category, decimal MonthlyLimit);
        private record ProgressBody(int BudgetId, string Category, decimal MonthlyLimit, decimal Spent, decimal Remaining, decimal PercentUsed);

        private static object NewTransaction(string type, decimal amount, string category, string date) =>
            new { type, amount, category, createdAt = $"{date}T00:00:00Z" };

        private static async Task<TransactionBody> CreateTransactionAsync(HttpClient client, object body)
        {
            var response = await client.PostAsJsonAsync("/api/Transactions/Create", body);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
        }

        // --- Authentication ---

        [Theory]
        [InlineData("/api/Transactions/All")]
        [InlineData("/api/Transactions/Summary")]
        [InlineData("/api/Budgets/All")]
        [InlineData("/api/Budgets/Progress")]
        public async Task ProtectedEndpoints_WithoutToken_Return401(string url)
        {
            var response = await factory.CreateClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ProtectedEndpoints_WithGarbageToken_Return401()
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", "not.a.real.token");

            var response = await client.GetAsync("/api/Transactions/All");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Register_DuplicateEmail_Returns400_AndLoginWithWrongPassword_Returns401()
        {
            var client = factory.CreateClient();
            var credentials = new { email = $"{Guid.NewGuid():N}@test.local", password = "TestPass123!" };

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/Register", credentials)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Auth/Register", credentials)).StatusCode);

            var wrong = await client.PostAsJsonAsync("/api/Auth/Login", new { credentials.email, password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            var right = await client.PostAsJsonAsync("/api/Auth/Login", credentials);
            Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        }

        // --- Authorization / per-user isolation ---

        [Fact]
        public async Task AnotherUsersTransaction_IsA404_ForReadUpdateAndDelete_AndStaysUntouched()
        {
            var owner = await factory.CreateAuthenticatedClientAsync();
            var intruder = await factory.CreateAuthenticatedClientAsync();
            var created = await CreateTransactionAsync(owner, NewTransaction("Expense", 42.50m, "Food", "2026-09-15"));

            Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/Transactions/Details/{created.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await intruder.PutAsJsonAsync($"/api/Transactions/Update/{created.Id}", NewTransaction("Expense", 1m, "Food", "2026-01-01"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/api/Transactions/Delete/{created.Id}")).StatusCode);

            var stillThere = await owner.GetFromJsonAsync<TransactionBody>($"/api/Transactions/Details/{created.Id}");
            Assert.Equal(42.50m, stillThere!.Amount);
        }

        [Fact]
        public async Task NotYoursAndDoesNotExist_AreIndistinguishable()
        {
            var owner = await factory.CreateAuthenticatedClientAsync();
            var intruder = await factory.CreateAuthenticatedClientAsync();
            var created = await CreateTransactionAsync(owner, NewTransaction("Expense", 5m, "Food", "2026-09-15"));

            var notYours = await intruder.GetAsync($"/api/Transactions/Details/{created.Id}");
            var missing = await intruder.GetAsync("/api/Transactions/Details/999999");

            Assert.Equal(missing.StatusCode, notYours.StatusCode);
            Assert.Equal(WithoutTraceId(await missing.Content.ReadAsStringAsync()), WithoutTraceId(await notYours.Content.ReadAsStringAsync()));
        }

        // ASP.NET adds a different traceId to every error response; everything else must match.
        private static string WithoutTraceId(string body) =>
            System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"\"");

        [Fact]
        public async Task ListAndSummary_OnlyIncludeTheCallersOwnTransactions()
        {
            var alice = await factory.CreateAuthenticatedClientAsync();
            var bob = await factory.CreateAuthenticatedClientAsync();
            await CreateTransactionAsync(alice, NewTransaction("Income", 100m, "Salary", "2026-09-01"));
            await CreateTransactionAsync(bob, NewTransaction("Income", 999m, "Salary", "2026-09-01"));

            var list = await alice.GetFromJsonAsync<PagedBody>("/api/Transactions/All");
            var summary = await alice.GetFromJsonAsync<SummaryBody>("/api/Transactions/Summary");

            Assert.Equal(1, list!.TotalCount);
            Assert.Equal(100m, summary!.TotalIncome);
        }

        // --- Behaviour of the transaction endpoints ---

        [Fact]
        public async Task UpdatingATransaction_PersistsItsEditedDate()
        {
            var client = await factory.CreateAuthenticatedClientAsync();
            var created = await CreateTransactionAsync(client, NewTransaction("Expense", 10m, "Food", "2026-01-01"));

            var update = await client.PutAsJsonAsync($"/api/Transactions/Update/{created.Id}", NewTransaction("Expense", 10m, "Food", "2026-06-15"));
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var reread = await client.GetFromJsonAsync<TransactionBody>($"/api/Transactions/Details/{created.Id}");
            Assert.Equal(new DateTime(2026, 6, 15), reread!.CreatedAt.Date);
        }

        [Fact]
        public async Task MonthFilter_ScopesListAndSummary_AndRejectsBadInput()
        {
            var client = await factory.CreateAuthenticatedClientAsync();
            await CreateTransactionAsync(client, NewTransaction("Expense", 10m, "Food", "2026-08-31"));
            await CreateTransactionAsync(client, NewTransaction("Expense", 20m, "Food", "2026-09-01"));
            await CreateTransactionAsync(client, NewTransaction("Income", 100m, "Salary", "2026-09-30"));
            await CreateTransactionAsync(client, NewTransaction("Expense", 40m, "Food", "2026-10-01"));

            var list = await client.GetFromJsonAsync<PagedBody>("/api/Transactions/All?year=2026&month=9");
            var summary = await client.GetFromJsonAsync<SummaryBody>("/api/Transactions/Summary?year=2026&month=9");

            Assert.Equal(2, list!.TotalCount);
            Assert.Equal(100m, summary!.TotalIncome);
            Assert.Equal(20m, summary.TotalExpenses);

            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/Transactions/All?year=2026")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/Transactions/Summary?year=2026&month=13")).StatusCode);
        }

        [Fact]
        public async Task PageSize_IsClampedServerSide()
        {
            var client = await factory.CreateAuthenticatedClientAsync();

            var list = await client.GetFromJsonAsync<PagedBody>("/api/Transactions/All?page=-3&pageSize=5000");

            Assert.Equal(1, list!.Page);
            Assert.Equal(10, list.PageSize);
        }

        // --- Budgets ---

        [Fact]
        public async Task Budgets_RejectDuplicatesAndInvalidInput()
        {
            var client = await factory.CreateAuthenticatedClientAsync();

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Budgets/Create", new { category = "Food", monthlyLimit = 100m })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/Budgets/Create", new { category = "Food", monthlyLimit = 5m })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Budgets/Create", new { category = "Rent", monthlyLimit = 0m })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Budgets/Create", new { category = "", monthlyLimit = 5m })).StatusCode);
        }

        [Fact]
        public async Task AnotherUsersBudget_IsInvisibleAndA404()
        {
            var owner = await factory.CreateAuthenticatedClientAsync();
            var intruder = await factory.CreateAuthenticatedClientAsync();
            var created = await (await owner.PostAsJsonAsync("/api/Budgets/Create", new { category = "Food", monthlyLimit = 100m }))
                .Content.ReadFromJsonAsync<BudgetBody>();

            Assert.Empty((await intruder.GetFromJsonAsync<List<BudgetBody>>("/api/Budgets/All"))!);
            Assert.Equal(HttpStatusCode.NotFound, (await intruder.PutAsJsonAsync($"/api/Budgets/Update/{created!.Id}", new { monthlyLimit = 1m })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/api/Budgets/Delete/{created.Id}")).StatusCode);
            Assert.Equal(100m, (await owner.GetFromJsonAsync<List<BudgetBody>>("/api/Budgets/All"))!.Single().MonthlyLimit);
        }

        [Fact]
        public async Task BudgetProgress_SumsOnlyThatMonthsExpensesInTheCategory()
        {
            var client = await factory.CreateAuthenticatedClientAsync();
            await client.PostAsJsonAsync("/api/Budgets/Create", new { category = "Food", monthlyLimit = 100m });
            await CreateTransactionAsync(client, NewTransaction("Expense", 10.10m, "Food", "2026-09-01"));
            await CreateTransactionAsync(client, NewTransaction("Expense", 20.20m, "Food", "2026-09-30"));
            await CreateTransactionAsync(client, NewTransaction("Expense", 99m, "Food", "2026-10-01"));
            await CreateTransactionAsync(client, NewTransaction("Income", 99m, "Food", "2026-09-10"));
            await CreateTransactionAsync(client, NewTransaction("Expense", 99m, "Shopping", "2026-09-10"));

            var progress = (await client.GetFromJsonAsync<List<ProgressBody>>("/api/Budgets/Progress?year=2026&month=9"))!.Single();

            Assert.Equal(30.30m, progress.Spent);
            Assert.Equal(69.70m, progress.Remaining);
            Assert.Equal(30.3m, progress.PercentUsed);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/Budgets/Progress?year=2026&month=13")).StatusCode);
        }
    }
}
