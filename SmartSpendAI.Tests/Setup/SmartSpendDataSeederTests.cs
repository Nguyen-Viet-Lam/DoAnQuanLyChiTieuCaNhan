using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;
using SmartSpendAI.Security;
using SmartSpendAI.Services.Setup;

namespace SmartSpendAI.Tests.Setup;

public sealed class SmartSpendDataSeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesBaselineData_WhenDatabaseIsEmpty()
    {
        await using var dbContext = CreateDbContext();
        var seeder = CreateSeeder(dbContext);

        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, await dbContext.Roles.CountAsync());
        Assert.Equal(8, await dbContext.Categories.CountAsync());
        Assert.Equal(2, await dbContext.Users.CountAsync());
        Assert.Equal(3, await dbContext.Wallets.CountAsync());
        Assert.Equal(20, await dbContext.Transactions.CountAsync());
        Assert.Equal(4, await dbContext.Budgets.CountAsync());
        Assert.Equal(3, await dbContext.BudgetAlerts.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_FillsMissingBaselineData_WhenDatabaseIsPartial()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Roles.Add(new Role { RoleName = AppRoles.StandardUser });
        dbContext.Categories.Add(new Category
        {
            Name = "An uong",
            Type = "Expense",
            Icon = "old-icon",
            Color = "#000000",
            IsSystem = false
        });
        await dbContext.SaveChangesAsync();

        var seeder = CreateSeeder(dbContext);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, await dbContext.Roles.CountAsync());
        Assert.Equal(8, await dbContext.Categories.CountAsync());

        var category = await dbContext.Categories.SingleAsync(x => x.Name == "An uong" && x.Type == "Expense");
        Assert.Equal("utensils", category.Icon);
        Assert.Equal("#ff7a18", category.Color);
        Assert.True(category.IsSystem);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenRunMultipleTimes()
    {
        await using var dbContext = CreateDbContext();
        var seeder = CreateSeeder(dbContext);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, await dbContext.Roles.CountAsync());
        Assert.Equal(8, await dbContext.Categories.CountAsync());
        Assert.Equal(2, await dbContext.Users.CountAsync());
        Assert.Equal(3, await dbContext.Wallets.CountAsync());
        Assert.Equal(20, await dbContext.Transactions.CountAsync());
        Assert.Equal(4, await dbContext.Budgets.CountAsync());
        Assert.Equal(3, await dbContext.BudgetAlerts.CountAsync());
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"smartspend-seeder-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static SmartSpendDataSeeder CreateSeeder(AppDbContext dbContext)
    {
        return new SmartSpendDataSeeder(
            dbContext,
            Options.Create(new SmartSpendSeedOptions
            {
                Enabled = true,
                SeedDemoData = true,
                ResetPasswordsOnSeed = false
            }),
            NullLogger<SmartSpendDataSeeder>.Instance);
    }
}
