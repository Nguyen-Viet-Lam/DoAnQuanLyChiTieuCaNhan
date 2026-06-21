using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;

namespace SmartSpendAI.Services.Email
{
    public class WeeklySummaryEmailHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly WeeklySummaryEmailSettings _settings;
        private readonly ILogger<WeeklySummaryEmailHostedService> _logger;
        private readonly TimeProvider _timeProvider;

        public WeeklySummaryEmailHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<WeeklySummaryEmailSettings> settings,
            ILogger<WeeklySummaryEmailHostedService> logger,
            TimeProvider? timeProvider = null)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("Dịch vụ email tổng kết tuần đang bị tắt.");
                return;
            }

            var intervalMinutes = Math.Clamp(_settings.CheckIntervalMinutes, 1, 60);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await TryDispatchWeeklySummariesAsync(intervalMinutes, stoppingToken);
            }
        }

        private async Task TryDispatchWeeklySummariesAsync(int intervalMinutes, CancellationToken cancellationToken)
        {
            var timeZone = ResolveTimeZone(_settings.TimeZoneId);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, timeZone);

            if (nowLocal.DayOfWeek != DayOfWeek.Monday)
            {
                return;
            }

            var scheduledHour = Math.Clamp(_settings.SendHourLocal, 0, 23);
            var scheduledMinute = Math.Clamp(_settings.SendMinuteLocal, 0, 59);
            var nowInMinutes = nowLocal.Hour * 60 + nowLocal.Minute;
            var startWindow = scheduledHour * 60 + scheduledMinute;
            var endWindow = startWindow + intervalMinutes;

            if (nowInMinutes < startWindow || nowInMinutes >= endWindow)
            {
                return;
            }

            var thisMondayLocal = nowLocal.Date;
            var previousMondayLocal = thisMondayLocal.AddDays(-7);
            var periodStartUtc = TimeZoneInfo.ConvertTimeToUtc(previousMondayLocal, timeZone);
            var periodEndUtc = TimeZoneInfo.ConvertTimeToUtc(thisMondayLocal, timeZone);
            var weekKey = $"{previousMondayLocal:yyyyMMdd}-{thisMondayLocal.AddDays(-1):yyyyMMdd}";

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var smtpSettings = scope.ServiceProvider.GetRequiredService<IOptions<SmtpSettings>>().Value;

            if (string.IsNullOrWhiteSpace(smtpSettings.Host) ||
                string.IsNullOrWhiteSpace(smtpSettings.Username) ||
                string.IsNullOrWhiteSpace(smtpSettings.Password))
            {
                _logger.LogWarning("Bỏ qua email tổng kết tuần vì SMTP chưa được cấu hình.");
                return;
            }

            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var users = await dbContext.Users
                .AsNoTracking()
                .Where(x => !x.IsLocked && x.IsEmailVerified)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email
                })
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                try
                {
                    var alreadySent = await dbContext.AuditLogs
                        .AsNoTracking()
                        .AnyAsync(
                            x => x.ActorUserId == user.UserId &&
                                 x.Action == "WeeklySummarySent" &&
                                 x.Metadata == weekKey,
                            cancellationToken);

                    if (alreadySent)
                    {
                        continue;
                    }

                    var transactions = await dbContext.Transactions
                        .AsNoTracking()
                        .Where(x => x.UserId == user.UserId &&
                                    x.TransactionDate >= periodStartUtc &&
                                    x.TransactionDate < periodEndUtc)
                        .ToListAsync(cancellationToken);

                    var categoryIds = transactions
                        .Select(x => x.CategoryId)
                        .Distinct()
                        .ToList();

                    var categoryNames = await dbContext.Categories
                        .AsNoTracking()
                        .Where(x => categoryIds.Contains(x.CategoryId))
                        .ToDictionaryAsync(x => x.CategoryId, x => x.Name, cancellationToken);

                    var totalIncome = transactions
                        .Where(x => string.Equals(x.Type, "Income", StringComparison.OrdinalIgnoreCase))
                        .Sum(x => x.Amount);

                    var totalExpense = transactions
                        .Where(x => string.Equals(x.Type, "Expense", StringComparison.OrdinalIgnoreCase))
                        .Sum(x => x.Amount);

                    var topExpenseCategories = transactions
                        .Where(x => string.Equals(x.Type, "Expense", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(x => GetExpenseCategoryName(x, categoryNames))
                        .Select(group => new ExpenseCategorySummary(group.Key, group.Sum(x => x.Amount)))
                        .OrderByDescending(x => x.Amount)
                        .Take(3)
                        .ToList();

                    var subject = $"Tong ket chi tieu tuan {previousMondayLocal:dd/MM} - {thisMondayLocal.AddDays(-1):dd/MM}";
                    var textBody = BuildTextBody(user.FullName, previousMondayLocal, thisMondayLocal, totalIncome, totalExpense, topExpenseCategories);
                    var htmlBody = BuildHtmlBody(user.FullName, previousMondayLocal, thisMondayLocal, totalIncome, totalExpense, topExpenseCategories);

                    await emailSender.SendAsync(user.Email, subject, htmlBody, textBody, cancellationToken);

                    dbContext.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = user.UserId,
                        Action = "WeeklySummarySent",
                        TargetType = "Email",
                        Metadata = weekKey,
                        CreatedAt = DateTime.UtcNow
                    });

                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể gửi email tổng kết tuần cho UserId={UserId}.", user.UserId);
                }
            }
        }

        private static string BuildTextBody(
            string fullName,
            DateTime previousMondayLocal,
            DateTime thisMondayLocal,
            decimal totalIncome,
            decimal totalExpense,
            IReadOnlyCollection<ExpenseCategorySummary> topExpenseCategories)
        {
            var lines = topExpenseCategories
                .Select(item => $"- {item.CategoryName}: {item.Amount:N0} VND")
                .ToList();

            var topSection = lines.Count > 0
                ? string.Join("\n", lines)
                : "- Chưa có giao dịch chi tiêu trong tuần.";

            return
                $"Xin chao {fullName},\n\n" +
                $"Day la tong ket chi tieu tuan {previousMondayLocal:dd/MM} - {thisMondayLocal.AddDays(-1):dd/MM}.\n" +
                $"Tong thu: {totalIncome:N0} VND\n" +
                $"Tong chi: {totalExpense:N0} VND\n" +
                $"Chenh lech: {(totalIncome - totalExpense):N0} VND\n\n" +
                "Top nhom chi tieu:\n" +
                $"{topSection}\n\n" +
                "SmartSpend AI";
        }

        private static string BuildHtmlBody(
            string fullName,
            DateTime previousMondayLocal,
            DateTime thisMondayLocal,
            decimal totalIncome,
            decimal totalExpense,
            IReadOnlyCollection<ExpenseCategorySummary> topExpenseCategories)
        {
            var topItems = topExpenseCategories
                .Select(item => $"<li><strong>{item.CategoryName}</strong>: {item.Amount:N0} VND</li>")
                .ToList();

            if (topItems.Count == 0)
            {
                topItems.Add("<li>Chưa có giao dịch chi tiêu trong tuần.</li>");
            }

            return
                $"<p>Xin chao <strong>{fullName}</strong>,</p>" +
                $"<p>Day la tong ket chi tieu tuan <strong>{previousMondayLocal:dd/MM}</strong> - <strong>{thisMondayLocal.AddDays(-1):dd/MM}</strong>.</p>" +
                "<ul>" +
                $"<li>Tong thu: <strong>{totalIncome:N0} VND</strong></li>" +
                $"<li>Tong chi: <strong>{totalExpense:N0} VND</strong></li>" +
                $"<li>Chenh lech: <strong>{(totalIncome - totalExpense):N0} VND</strong></li>" +
                "</ul>" +
                "<p>Top nhom chi tieu:</p>" +
                $"<ul>{string.Join(string.Empty, topItems)}</ul>" +
                "<p>SmartSpend AI</p>";
        }

        private TimeZoneInfo ResolveTimeZone(string timeZoneId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch
            {
                _logger.LogWarning("Múi giờ WeeklySummaryEmail '{TimeZoneId}' không hợp lệ. Sẽ dùng UTC thay thế.", timeZoneId);
                return TimeZoneInfo.Utc;
            }
        }

        private static string GetExpenseCategoryName(
            TransactionEntry transaction,
            IReadOnlyDictionary<int, string> categoryNames)
        {
            return !categoryNames.TryGetValue(transaction.CategoryId, out var categoryName) ||
                   string.IsNullOrWhiteSpace(categoryName)
                ? "Không phân loại"
                : categoryName;
        }

        private sealed record ExpenseCategorySummary(string CategoryName, decimal Amount);
    }
}
