using System.Reflection;
using SmartSpendAI.Controllers;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Dashboard;

namespace SmartSpendAI.Tests.Dashboard;

public sealed class DashboardForecastTests
{
    [Fact]
    public void BuildForecastSummary_SeparatesRecurringFixedCosts_FromFlexibleSpending()
    {
        var budgetMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var category = new Category
        {
            CategoryId = 4,
            Name = "Hoa don",
            Type = "Expense",
            Color = "#ff4d4f",
            Icon = "receipt",
            IsSystem = true
        };

        var currentMonthTransactions = new List<TransactionEntry>
        {
            new()
            {
                TransactionEntryId = 1,
                CategoryId = 4,
                Category = category,
                Type = "Expense",
                Amount = 2_000_000m,
                Note = "Tien tro",
                TransactionDate = budgetMonth.AddDays(1)
            },
            new()
            {
                TransactionEntryId = 2,
                CategoryId = 1,
                Category = new Category { CategoryId = 1, Name = "An uong", Type = "Expense", Color = "#ff7a18", Icon = "utensils", IsSystem = true },
                Type = "Expense",
                Amount = 120_000m,
                Note = "An vat cuoi tuan",
                TransactionDate = budgetMonth.AddDays(3)
            }
        };

        var historicalTransactions = new List<TransactionEntry>
        {
            CreateHistoricalExpense(10, category, 2_000_000m, "Tien tro", budgetMonth.AddMonths(-1).AddDays(1)),
            CreateHistoricalExpense(11, category, 2_050_000m, "Tien tro", budgetMonth.AddMonths(-2).AddDays(2)),
            CreateHistoricalExpense(12, new Category { CategoryId = 5, Name = "Hoa don", Type = "Expense", Color = "#ff4d4f", Icon = "receipt", IsSystem = true }, 500_000m, "Tien dien", budgetMonth.AddMonths(-1).AddDays(2)),
            CreateHistoricalExpense(13, new Category { CategoryId = 5, Name = "Hoa don", Type = "Expense", Color = "#ff4d4f", Icon = "receipt", IsSystem = true }, 480_000m, "Tien dien", budgetMonth.AddMonths(-2).AddDays(3)),
            CreateHistoricalExpense(14, new Category { CategoryId = 1, Name = "An uong", Type = "Expense", Color = "#ff7a18", Icon = "utensils", IsSystem = true }, 100_000m, "Cafe sang", budgetMonth.AddMonths(-1).AddDays(4)),
            CreateHistoricalExpense(15, new Category { CategoryId = 1, Name = "An uong", Type = "Expense", Color = "#ff7a18", Icon = "utensils", IsSystem = true }, 110_000m, "Tra sua", budgetMonth.AddMonths(-2).AddDays(5))
        };

        var wallets = new List<Wallet>
        {
            new() { WalletId = 1, Name = "Tien mat", Balance = 5_000_000m }
        };

        var summary = InvokeBuildForecastSummary(wallets, currentMonthTransactions, historicalTransactions, []);

        Assert.NotNull(summary);
        Assert.True(summary.FixedSpentThisMonth >= 2_000_000m);
        Assert.True(summary.FixedRemainingThisMonth >= 450_000m);
        Assert.True(summary.FlexibleProjectedRemaining >= 0m);
        Assert.Contains(summary.FixedCostItems, item => item.Label.Contains("tien tro", StringComparison.OrdinalIgnoreCase) && item.AlreadyCharged);
        Assert.Contains(summary.FixedCostItems, item => item.Label.Contains("tien dien", StringComparison.OrdinalIgnoreCase) && !item.AlreadyCharged);
        Assert.NotEmpty(summary.Explanations);
    }

    [Fact]
    public void BuildForecasts_UsesForecastSummary_ForHumanReadableDashboardLines()
    {
        var summary = new ForecastSummaryDto
        {
            FixedSpentThisMonth = 2_000_000m,
            FixedRemainingThisMonth = 500_000m,
            FlexibleProjectedRemaining = 1_200_000m,
            ProjectedEndBalance = 3_300_000m
        };

        var forecasts = InvokeBuildForecasts(summary, []);

        Assert.Contains(forecasts, item => item.Contains("Chi phi co dinh con lai", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(forecasts, item => item.Contains("Chi tieu linh hoat con lai", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(forecasts, item => item.Contains("So du cuoi thang", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildForecastSummary_UsesNormalMonthlyBaseline_WhenDataIsSparse()
    {
        var wallets = new List<Wallet>
        {
            new() { WalletId = 1, Name = "Tien mat", Balance = 1_000_000m }
        };

        var summary = InvokeBuildForecastSummary(wallets, [], [], []);

        Assert.Equal(500_000m, summary.FlexibleProjectedRemaining);
        Assert.Contains(summary.Explanations, item => item.Contains("500,000", StringComparison.OrdinalIgnoreCase));
    }

    private static TransactionEntry CreateHistoricalExpense(int id, Category category, decimal amount, string note, DateTime transactionDate)
    {
        return new TransactionEntry
        {
            TransactionEntryId = id,
            CategoryId = category.CategoryId,
            Category = category,
            Type = "Expense",
            Amount = amount,
            Note = note,
            TransactionDate = transactionDate
        };
    }

    private static ForecastSummaryDto InvokeBuildForecastSummary(
        List<Wallet> wallets,
        List<TransactionEntry> currentMonthTransactions,
        List<TransactionEntry> historicalTransactions,
        List<Budget> budgets)
    {
        var method = typeof(DashboardController)
            .GetMethod("BuildForecastSummary", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (ForecastSummaryDto)method.Invoke(null, [wallets, currentMonthTransactions, historicalTransactions, budgets])!;
    }

    private static List<string> InvokeBuildForecasts(ForecastSummaryDto summary, List<Budget> budgets)
    {
        var method = typeof(DashboardController)
            .GetMethod("BuildForecasts", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (List<string>)method.Invoke(null, [summary, budgets])!;
    }
}
