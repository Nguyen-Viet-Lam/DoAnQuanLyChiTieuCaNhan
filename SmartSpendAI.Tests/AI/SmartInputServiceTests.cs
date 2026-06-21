using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Services.AI;

namespace SmartSpendAI.Tests.AI;

public sealed class SmartInputServiceTests
{
    [Fact]
    public async Task ParseAsync_PrioritizesPersonalKeywordOverSystemKeyword()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Categories.AddRange(
            new Category { CategoryId = 1, Name = "An uong", Type = "Expense", Icon = "utensils", Color = "#ff7a18", IsSystem = true },
            new Category { CategoryId = 2, Name = "Di chuyen", Type = "Expense", Icon = "car", Color = "#00b894", IsSystem = true });
        dbContext.Keywords.Add(new KeywordEntry
        {
            KeywordEntryId = 1,
            Word = "xang",
            CategoryId = 2,
            Weight = 10,
            IsActive = true
        });
        dbContext.UserPersonalKeywords.Add(new UserPersonalKeyword
        {
            UserPersonalKeywordId = 1,
            UserId = 7,
            CategoryId = 1,
            Keyword = "xang",
            UsageCount = 3
        });
        await dbContext.SaveChangesAsync();

        var service = new SmartInputService(dbContext);
        var result = await service.ParseAsync("Do xang 200k", 7, CancellationToken.None);

        Assert.Equal(1, result.SuggestedCategoryId);
        Assert.Contains("xang", result.MatchedKeywords);
    }

    [Fact]
    public async Task LearnFromCorrectionAsync_UpsertsPersonalKeyword()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Categories.AddRange(
            new Category { CategoryId = 10, Name = "Khac", Type = "Expense", Icon = "circle", Color = "#94a3b8", IsSystem = true },
            new Category { CategoryId = 11, Name = "Hoc tap", Type = "Expense", Icon = "book", Color = "#2563eb", IsSystem = true });
        await dbContext.SaveChangesAsync();

        var service = new SmartInputService(dbContext);
        await service.LearnFromCorrectionAsync("Mua sach 150k", 22, 10, CancellationToken.None);
        await service.LearnFromCorrectionAsync("Mua sach 150k", 22, 11, CancellationToken.None);

        var learned = await dbContext.UserPersonalKeywords
            .SingleAsync(x => x.UserId == 22 && x.Keyword == "mua sach 150k");

        Assert.Equal(11, learned.CategoryId);
        Assert.Equal(2, learned.UsageCount);
    }

    [Fact]
    public async Task ParseAsync_UsesTransactionHistory_ToSuggestWalletAndType()
    {
        await using var dbContext = CreateDbContext();
        var foodCategory = new Category { CategoryId = 1, Name = "An uong", Type = "Expense", Icon = "utensils", Color = "#ff7a18", IsSystem = true };
        var incomeCategory = new Category { CategoryId = 2, Name = "Luong", Type = "Income", Icon = "wallet", Color = "#16a34a", IsSystem = true };
        var cashWallet = new Wallet { WalletId = 10, UserId = 7, Name = "Tien mat", Type = "Cash", Balance = 1_000_000m, IsDefault = false, CreatedAt = DateTime.UtcNow };
        var bankWallet = new Wallet { WalletId = 11, UserId = 7, Name = "Tai khoan ngan hang", Type = "Bank", Balance = 5_000_000m, IsDefault = true, CreatedAt = DateTime.UtcNow };

        dbContext.Categories.AddRange(foodCategory, incomeCategory);
        dbContext.Wallets.AddRange(cashWallet, bankWallet);
        dbContext.Transactions.AddRange(
            new TransactionEntry
            {
                TransactionEntryId = 1,
                UserId = 7,
                WalletId = 10,
                Wallet = cashWallet,
                CategoryId = 1,
                Category = foodCategory,
                Type = "Expense",
                Amount = 45_000m,
                Note = "An trua van phong",
                TransactionDate = DateTime.UtcNow.AddDays(-5),
                CreatedAt = DateTime.UtcNow
            },
            new TransactionEntry
            {
                TransactionEntryId = 2,
                UserId = 7,
                WalletId = 11,
                Wallet = bankWallet,
                CategoryId = 2,
                Category = incomeCategory,
                Type = "Income",
                Amount = 15_000_000m,
                Note = "Luong thang nay",
                TransactionDate = DateTime.UtcNow.AddDays(-3),
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new SmartInputService(dbContext);
        var result = await service.ParseAsync("An trua 45k", 7, CancellationToken.None);

        Assert.Equal(1, result.SuggestedCategoryId);
        Assert.Equal("Expense", result.SuggestedType);
        Assert.Equal(10, result.SuggestedWalletId);
        Assert.Equal("Tien mat", result.SuggestedWalletName);
        Assert.Contains(result.Reasoning, item => item.Contains("lịch sử giao dịch", StringComparison.OrdinalIgnoreCase));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"smart-input-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }
}
