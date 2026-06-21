using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;
using SmartSpendAI.Services.Email;

namespace SmartSpendAI.Tests.Email;

public sealed class WeeklySummaryEmailHostedServiceTests
{
    [Fact]
    public async Task TryDispatchWeeklySummariesAsync_Skips_WhenSmtpIsNotConfigured()
    {
        var databaseName = $"weekly-summary-no-smtp-{Guid.NewGuid()}";
        var emailSender = new RecordingEmailSender();
        using var serviceProvider = BuildServiceProvider(
            databaseName,
            emailSender,
            new SmtpSettings(),
            new WeeklySummaryEmailSettings
            {
                Enabled = true,
                TimeZoneId = "UTC",
                SendHourLocal = 8,
                SendMinuteLocal = 0,
                CheckIntervalMinutes = 10
            });

        await SeedWeeklySummaryDataAsync(serviceProvider, includeMissingCategory: false);

        var service = new WeeklySummaryEmailHostedService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WeeklySummaryEmailSettings
            {
                Enabled = true,
                TimeZoneId = "UTC",
                SendHourLocal = 8,
                SendMinuteLocal = 0,
                CheckIntervalMinutes = 10
            }),
            NullLogger<WeeklySummaryEmailHostedService>.Instance,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 8, 5, 0, TimeSpan.Zero)));

        await InvokeDispatchAsync(service, 10);

        Assert.Empty(emailSender.SentMessages);
    }

    [Fact]
    public async Task TryDispatchWeeklySummariesAsync_UsesFallbackCategoryName_WhenCategoryIsMissing()
    {
        var databaseName = $"weekly-summary-missing-category-{Guid.NewGuid()}";
        var emailSender = new RecordingEmailSender();
        using var serviceProvider = BuildServiceProvider(
            databaseName,
            emailSender,
            new SmtpSettings
            {
                Host = "smtp.example.com",
                Username = "tester",
                Password = "secret"
            },
            new WeeklySummaryEmailSettings
            {
                Enabled = true,
                TimeZoneId = "UTC",
                SendHourLocal = 8,
                SendMinuteLocal = 0,
                CheckIntervalMinutes = 10
            });

        await SeedWeeklySummaryDataAsync(serviceProvider, includeMissingCategory: true);

        var service = new WeeklySummaryEmailHostedService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WeeklySummaryEmailSettings
            {
                Enabled = true,
                TimeZoneId = "UTC",
                SendHourLocal = 8,
                SendMinuteLocal = 0,
                CheckIntervalMinutes = 10
            }),
            NullLogger<WeeklySummaryEmailHostedService>.Instance,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 8, 5, 0, TimeSpan.Zero)));

        await InvokeDispatchAsync(service, 10);

        var sentMessage = Assert.Single(emailSender.SentMessages);
        Assert.True(sentMessage.HtmlBody.Contains("Không phân loại", StringComparison.Ordinal), sentMessage.HtmlBody);
        Assert.True(sentMessage.TextBody.Contains("Không phân loại", StringComparison.Ordinal), sentMessage.TextBody);
    }

    private static ServiceProvider BuildServiceProvider(
        string databaseName,
        IEmailSender emailSender,
        SmtpSettings smtpSettings,
        WeeklySummaryEmailSettings weeklySettings)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(emailSender);
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddSingleton<IOptions<SmtpSettings>>(Options.Create(smtpSettings));
        services.AddSingleton<IOptions<WeeklySummaryEmailSettings>>(Options.Create(weeklySettings));
        return services.BuildServiceProvider();
    }

    private static async Task SeedWeeklySummaryDataAsync(IServiceProvider serviceProvider, bool includeMissingCategory)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Users.Add(new User
        {
            UserId = 10,
            Username = "weekly.user",
            FullName = "Weekly User",
            Email = "weekly.user@example.com",
            PasswordHash = "hash",
            RoleId = 1,
            IsEmailVerified = true,
            CreatedAt = DateTime.UtcNow
        });

        dbContext.Categories.Add(new Category
        {
            CategoryId = 1,
            Name = "An uong",
            Type = "Expense",
            Icon = "utensils",
            Color = "#ff7a18",
            IsSystem = true
        });

        dbContext.Transactions.Add(new TransactionEntry
        {
            TransactionEntryId = 1,
            UserId = 10,
            WalletId = 1,
            CategoryId = includeMissingCategory ? 999 : 1,
            Type = "Expense",
            Amount = 120_000m,
            Note = "Weekly expense",
            TransactionDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task InvokeDispatchAsync(WeeklySummaryEmailHostedService service, int intervalMinutes)
    {
        var method = typeof(WeeklySummaryEmailHostedService)
            .GetMethod("TryDispatchWeeklySummariesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var task = (Task)method.Invoke(service, [intervalMinutes, CancellationToken.None])!;
        await task;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<SentMessage> SentMessages { get; } = [];

        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
        {
            SentMessages.Add(new SentMessage(toEmail, subject, htmlBody, textBody));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record SentMessage(string ToEmail, string Subject, string HtmlBody, string TextBody);
}
