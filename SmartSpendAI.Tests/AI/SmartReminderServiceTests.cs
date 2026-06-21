using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Services.AI;

namespace SmartSpendAI.Tests.AI;

public sealed class SmartReminderServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetMonthlyRemindersAsync_ReturnsWeightedForecastAnomalyAndRecommendations()
    {
        await using var dbContext = CreateDbContext();
        SeedReminderScenario(dbContext, FixedUtcNow.UtcDateTime);

        var service = new SmartReminderService(dbContext, new FixedTimeProvider(FixedUtcNow));
        var reminders = await service.GetMonthlyRemindersAsync(42, CancellationToken.None);

        Assert.Contains(reminders, item => item.Type == "Budget" && string.Equals(item.CategoryName, "An uong", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reminders, item => item.Type == "Forecast" && item.Algorithm == "WeightedMovingAverage");
        Assert.Contains(reminders, item => item.Type == "Anomaly" && string.Equals(item.CategoryName, "Mua sam", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reminders, item => item.Type == "Recommendation" && !string.IsNullOrWhiteSpace(item.SuggestedAction));
        Assert.All(reminders.Where(item => !string.IsNullOrWhiteSpace(item.Explanation)), item => Assert.NotNull(item.Algorithm));
        Assert.True(reminders.Count <= 10);
    }

    [Fact]
    public async Task GetMonthlyRemindersAsync_AddsInactiveReminderWhenNoRecentTransaction()
    {
        await using var dbContext = CreateDbContext();
        SeedInactiveScenario(dbContext, FixedUtcNow.UtcDateTime);

        var service = new SmartReminderService(dbContext, new FixedTimeProvider(FixedUtcNow));
        var reminders = await service.GetMonthlyRemindersAsync(77, CancellationToken.None);

        Assert.Contains(reminders, item => item.Type == "Activity" && item.Message.Contains("14 ngày", StringComparison.OrdinalIgnoreCase));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"smart-reminder-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static void SeedReminderScenario(AppDbContext dbContext, DateTime now)
    {
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var userId = 42;

        dbContext.Roles.Add(new Role { RoleId = 1, RoleName = "StandardUser" });
        dbContext.Users.Add(new User
        {
            UserId = userId,
            Username = "demo42",
            FullName = "Demo 42",
            Email = "demo42@local",
            PasswordHash = "hash",
            RoleId = 1,
            CreatedAt = now
        });

        dbContext.Wallets.Add(new Wallet
        {
            WalletId = 1,
            UserId = userId,
            Name = "Tien mat",
            Type = "Cash",
            Balance = 5_000_000m,
            IsDefault = true,
            CreatedAt = now
        });

        var food = new Category { CategoryId = 1, Name = "An uong", Type = "Expense", Icon = "utensils", Color = "#ff7a18", IsSystem = true };
        var transport = new Category { CategoryId = 2, Name = "Di chuyen", Type = "Expense", Icon = "car", Color = "#00b894", IsSystem = true };
        var shopping = new Category { CategoryId = 3, Name = "Mua sam", Type = "Expense", Icon = "bag", Color = "#0097e6", IsSystem = true };
        dbContext.Categories.AddRange(food, transport, shopping);

        dbContext.Budgets.AddRange(
            new Budget
            {
                BudgetId = 1,
                UserId = userId,
                CategoryId = food.CategoryId,
                Month = monthStart,
                LimitAmount = 300_000m
            },
            new Budget
            {
                BudgetId = 2,
                UserId = userId,
                CategoryId = transport.CategoryId,
                Month = monthStart,
                LimitAmount = 100_000m
            },
            new Budget
            {
                BudgetId = 3,
                UserId = userId,
                CategoryId = shopping.CategoryId,
                Month = monthStart,
                LimitAmount = 300_000m
            });

        dbContext.Transactions.AddRange(
            CreateExpense(1, userId, 1, food, 120_000m, "An trua", new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(2, userId, 1, food, 130_000m, "An toi", new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(3, userId, 1, food, 90_000m, "Cafe", new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(4, userId, 1, transport, 85_000m, "Do xang", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(5, userId, 1, transport, 35_000m, "Gui xe", new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(6, userId, 1, shopping, 500_000m, "Mua giay", new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(7, userId, 1, food, 150_000m, "An uong thang truoc", new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(8, userId, 1, food, 140_000m, "An uong thang truoc 2", new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(9, userId, 1, food, 160_000m, "An uong thang truoc 3", new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(10, userId, 1, transport, 40_000m, "Di chuyen thang truoc", new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(11, userId, 1, transport, 45_000m, "Di chuyen thang truoc 2", new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(12, userId, 1, shopping, 60_000m, "Mua sam tuan 1", new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(13, userId, 1, shopping, 55_000m, "Mua sam tuan 2", new DateTime(2026, 5, 23, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(14, userId, 1, shopping, 50_000m, "Mua sam tuan 3", new DateTime(2026, 5, 16, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(15, userId, 1, shopping, 45_000m, "Mua sam tuan 4", new DateTime(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(16, userId, 1, food, 300_000m, "An lien hoan", new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc), now),
            CreateExpense(17, userId, 1, food, 50_000m, "An vat", new DateTime(2026, 5, 22, 0, 0, 0, DateTimeKind.Utc), now));

        dbContext.SaveChanges();
    }

    private static void SeedInactiveScenario(AppDbContext dbContext, DateTime now)
    {
        var userId = 77;

        dbContext.Roles.Add(new Role { RoleId = 1, RoleName = "StandardUser" });
        dbContext.Users.Add(new User
        {
            UserId = userId,
            Username = "demo77",
            FullName = "Demo 77",
            Email = "demo77@local",
            PasswordHash = "hash",
            RoleId = 1,
            CreatedAt = now
        });

        dbContext.Wallets.Add(new Wallet
        {
            WalletId = 1,
            UserId = userId,
            Name = "Tien mat",
            Type = "Cash",
            Balance = 1_000_000m,
            IsDefault = true,
            CreatedAt = now
        });

        var category = new Category
        {
            CategoryId = 1,
            Name = "Khac",
            Type = "Expense",
            Icon = "circle",
            Color = "#95a5a6",
            IsSystem = true
        };

        dbContext.Categories.Add(category);
        dbContext.Transactions.Add(CreateExpense(
            1,
            userId,
            1,
            category,
            50_000m,
            "Giao dich cu",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            now));

        dbContext.SaveChanges();
    }

    private static TransactionEntry CreateExpense(
        int id,
        int userId,
        int walletId,
        Category category,
        decimal amount,
        string note,
        DateTime transactionDate,
        DateTime createdAt)
    {
        return new TransactionEntry
        {
            TransactionEntryId = id,
            UserId = userId,
            WalletId = walletId,
            CategoryId = category.CategoryId,
            Category = category,
            Type = "Expense",
            Amount = amount,
            Note = note,
            TransactionDate = transactionDate,
            CreatedAt = createdAt
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
