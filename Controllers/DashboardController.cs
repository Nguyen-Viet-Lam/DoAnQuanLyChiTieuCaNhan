using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Dashboard;
using SmartSpendAI.Models.Dtos.Finance;
using SmartSpendAI.Services.AI;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartSpendAI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class DashboardController : ApiControllerBase
    {
        private const decimal NormalMonthlyVariableSpend = 500_000m;
        private readonly AppDbContext _dbContext;
        private readonly ISmartReminderService _smartReminderService;

        public DashboardController(AppDbContext dbContext, ISmartReminderService smartReminderService)
        {
            _dbContext = dbContext;
            _smartReminderService = smartReminderService;
        }

        [HttpGet]
        public async Task<ActionResult<DashboardResponse>> GetDashboard(CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            var previousMonthStart = monthStart.AddMonths(-1);
            var forecastHistoryStart = monthStart.AddMonths(-3);

            var wallets = await _dbContext.Wallets.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken);
            var monthTransactions = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.TransactionDate >= monthStart && x.TransactionDate < monthEnd)
                .ToListAsync(cancellationToken);

            var previousMonthTransactions = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.TransactionDate >= previousMonthStart && x.TransactionDate < monthStart)
                .ToListAsync(cancellationToken);

            var forecastHistoryTransactions = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.TransactionDate >= forecastHistoryStart && x.TransactionDate < monthStart)
                .ToListAsync(cancellationToken);

            var budgets = await _dbContext.Budgets
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.Month == monthStart)
                .ToListAsync(cancellationToken);

            var response = new DashboardResponse
            {
                TotalBalance = wallets.Sum(x => x.Balance),
                TotalIncomeThisMonth = monthTransactions.Where(x => x.Type == "Income").Sum(x => x.Amount),
                TotalExpenseThisMonth = monthTransactions.Where(x => x.Type == "Expense").Sum(x => x.Amount),
                UnreadAlerts = await _dbContext.BudgetAlerts.CountAsync(x => x.UserId == userId && !x.IsRead, cancellationToken),
                MonthlyTrend = await BuildMonthlyTrendAsync(userId.Value, cancellationToken),
                ExpenseBreakdown = monthTransactions
                    .Where(x => x.Type == "Expense")
                    .GroupBy(x => new { x.Category.Name, x.Category.Color })
                    .Select(g => new CategoryBreakdownDto
                    {
                        CategoryName = g.Key.Name,
                        Color = g.Key.Color,
                        Amount = g.Sum(x => x.Amount)
                    })
                    .OrderByDescending(x => x.Amount)
                    .ToList(),
                BudgetProgress = budgets.Select(budget =>
                {
                    var spent = monthTransactions
                        .Where(x => x.Type == "Expense" && x.CategoryId == budget.CategoryId)
                        .Sum(x => x.Amount);
                    var progress = budget.LimitAmount <= 0 ? 0 : decimal.Round(spent / budget.LimitAmount * 100, 2);
                    return new BudgetResponse
                    {
                        BudgetId = budget.BudgetId,
                        CategoryId = budget.CategoryId,
                        CategoryName = budget.Category.Name,
                        CategoryColor = budget.Category.Color,
                        Month = budget.Month,
                        LimitAmount = budget.LimitAmount,
                        SpentAmount = spent,
                        ProgressPercentage = progress,
                        Status = progress >= 100 ? "Danger" : progress >= 80 ? "Warning" : "Safe"
                    };
                }).OrderByDescending(x => x.ProgressPercentage).ToList(),
                Insights = BuildInsights(monthTransactions, previousMonthTransactions),
                ForecastSummary = BuildForecastSummary(wallets, monthTransactions, forecastHistoryTransactions, budgets),
                AiReminders = await _smartReminderService.GetMonthlyRemindersAsync(userId.Value, cancellationToken)
            };

            response.Forecasts = BuildForecasts(response.ForecastSummary, budgets);

            return Ok(response);
        }

        private async Task<List<TrendPointDto>> BuildMonthlyTrendAsync(int userId, CancellationToken cancellationToken)
        {
            var points = new List<TrendPointDto>();
            var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5);

            for (var i = 0; i < 6; i++)
            {
                var monthStart = start.AddMonths(i);
                var monthEnd = monthStart.AddMonths(1);

                var transactions = await _dbContext.Transactions
                    .AsNoTracking()
                    .Where(x => x.UserId == userId && x.TransactionDate >= monthStart && x.TransactionDate < monthEnd)
                    .ToListAsync(cancellationToken);

                points.Add(new TrendPointDto
                {
                    Label = monthStart.ToString("MM/yyyy"),
                    Income = transactions.Where(x => x.Type == "Income").Sum(x => x.Amount),
                    Expense = transactions.Where(x => x.Type == "Expense").Sum(x => x.Amount)
                });
            }

            return points;
        }

        private static List<string> BuildInsights(List<TransactionEntry> currentMonth, List<TransactionEntry> previousMonth)
        {
            var insights = new List<string>();
            var currentExpense = currentMonth.Where(x => x.Type == "Expense").Sum(x => x.Amount);
            var previousExpense = previousMonth.Where(x => x.Type == "Expense").Sum(x => x.Amount);

            if (previousExpense > 0)
            {
                var change = decimal.Round((currentExpense - previousExpense) / previousExpense * 100, 0);
                if (Math.Abs(change) >= 10)
                {
                    insights.Add($"Tổng chi tiêu tháng này {(change >= 0 ? "tăng" : "giảm")} {Math.Abs(change)}% so với tháng trước.");
                }
            }

            var topIncrease = currentMonth
                .Where(x => x.Type == "Expense")
                .GroupBy(x => x.Category.Name)
                .Select(group =>
                {
                    var currentAmount = group.Sum(x => x.Amount);
                    var previousAmount = previousMonth
                        .Where(x => x.Type == "Expense" && x.Category.Name == group.Key)
                        .Sum(x => x.Amount);
                    return new
                    {
                        CategoryName = group.Key,
                        Delta = currentAmount - previousAmount
                    };
                })
                .OrderByDescending(x => x.Delta)
                .FirstOrDefault();

            if (topIncrease is not null && topIncrease.Delta > 0)
            {
                insights.Add($"Nhóm {topIncrease.CategoryName} đang tăng mạnh trong tháng này. Bạn nên theo dõi sát hơn.");
            }

            if (insights.Count == 0)
            {
                insights.Add("Chi tiêu của bạn đang ổn định. Hãy tiếp tục cập nhật giao dịch đều đặn để AI đưa gợi ý tốt hơn.");
            }

            return insights;
        }

        private static ForecastSummaryDto BuildForecastSummary(
            List<Wallet> wallets,
            List<TransactionEntry> monthTransactions,
            List<TransactionEntry> historicalTransactions,
            List<Budget> budgets)
        {
            var today = DateTime.UtcNow.Date;
            var elapsedDays = Math.Max(1, today.Day);
            var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            var daysRemaining = Math.Max(0, daysInMonth - today.Day);

            var fixedSignatures = IdentifyRecurringCharges(historicalTransactions, today);
            var thisMonthFixedMatches = MatchCurrentMonthFixedCharges(monthTransactions, fixedSignatures);

            var fixedSpentThisMonth = thisMonthFixedMatches
                .Where(x => x.AlreadyCharged)
                .Sum(x => x.ExpectedAmount);

            var fixedRemainingThisMonth = thisMonthFixedMatches
                .Where(x => !x.AlreadyCharged)
                .Sum(x => x.ExpectedAmount);

            var flexibleCurrentTransactions = monthTransactions
                .Where(x => x.Type == "Expense" && !IsMatchedAsFixedCharge(x, thisMonthFixedMatches))
                .ToList();

            var flexibleHistoryTransactions = historicalTransactions
                .Where(x => x.Type == "Expense")
                .Where(x => !MatchesHistoricalFixedSignature(x, fixedSignatures))
                .ToList();

            var currentFlexibleDailyAverage = flexibleCurrentTransactions.Sum(x => x.Amount) / elapsedDays;
            var historicalFlexibleDailyAverage = CalculateWeightedHistoricalFlexibleDailyAverage(flexibleHistoryTransactions);
            var blendedFlexibleDailyAverage = currentFlexibleDailyAverage > 0 && historicalFlexibleDailyAverage > 0
                ? decimal.Round((currentFlexibleDailyAverage * 0.55m) + (historicalFlexibleDailyAverage * 0.45m), 2)
                : decimal.Round(Math.Max(currentFlexibleDailyAverage, historicalFlexibleDailyAverage), 2);

            var flexibleProjectedRemaining = decimal.Round(blendedFlexibleDailyAverage * daysRemaining, 0);
            if (flexibleProjectedRemaining <= 0m || flexibleProjectedRemaining < NormalMonthlyVariableSpend)
            {
                flexibleProjectedRemaining = NormalMonthlyVariableSpend;
            }

            var totalSpentThisMonth = monthTransactions.Where(x => x.Type == "Expense").Sum(x => x.Amount);
            var projectedTotalExpense = totalSpentThisMonth + fixedRemainingThisMonth + flexibleProjectedRemaining;
            var currentBalance = wallets.Sum(x => x.Balance);
            var projectedEndBalance = currentBalance - fixedRemainingThisMonth - flexibleProjectedRemaining;

            var explanations = new List<string>();
            if (fixedSignatures.Count > 0)
            {
                explanations.Add($"AI nhan dien {fixedSignatures.Count} khoan chi co tinh chat dinh ky dua tren 2-3 thang lich su.");
            }
            else
            {
                explanations.Add("AI chưa nhận diện đủ khoản chi định kỳ từ lịch sử gần đây.");
            }

            explanations.Add(
                fixedRemainingThisMonth > 0
                    ? $"Chi phí cố định còn lại ước tính: {fixedRemainingThisMonth:N0} VND."
                    : "Không còn khoản chi cố định nào chưa ghi nhận trong tháng này.");

            explanations.Add(
                blendedFlexibleDailyAverage > 0
                    ? $"Chi tiêu linh hoạt được dự báo bằng weighted moving average: nhịp hiện tại {currentFlexibleDailyAverage:N0}/ngày và lịch sử có trọng số {historicalFlexibleDailyAverage:N0}/ngày. Mốc bình thường tạm dùng là {NormalMonthlyVariableSpend:N0} VND/tháng."
                    : $"Chưa đủ dữ liệu chi tiêu linh hoạt. Tạm dùng mốc bình thường {NormalMonthlyVariableSpend:N0} VND/tháng để dự báo phần còn lại.");

            return new ForecastSummaryDto
            {
                FixedSpentThisMonth = decimal.Round(fixedSpentThisMonth, 0),
                FixedRemainingThisMonth = decimal.Round(fixedRemainingThisMonth, 0),
                FlexibleSpentThisMonth = decimal.Round(flexibleCurrentTransactions.Sum(x => x.Amount), 0),
                FlexibleProjectedRemaining = flexibleProjectedRemaining,
                ProjectedTotalExpense = decimal.Round(projectedTotalExpense, 0),
                ProjectedEndBalance = decimal.Round(projectedEndBalance, 0),
                CurrentFlexibleDailyAverage = decimal.Round(currentFlexibleDailyAverage, 0),
                HistoricalFlexibleDailyAverage = decimal.Round(historicalFlexibleDailyAverage, 0),
                Explanations = explanations,
                FixedCostItems = thisMonthFixedMatches
                    .OrderBy(x => x.ExpectedDayOfMonth ?? int.MaxValue)
                    .Select(x => new ForecastBreakdownItemDto
                    {
                        Label = x.Label,
                        Amount = decimal.Round(x.ExpectedAmount, 0),
                        AlreadyCharged = x.AlreadyCharged,
                        ExpectedDayOfMonth = x.ExpectedDayOfMonth
                    })
                    .ToList()
            };
        }

        private static List<string> BuildForecasts(ForecastSummaryDto? forecastSummary, List<Budget> budgets)
        {
            var forecasts = new List<string>();

            if (forecastSummary is null)
            {
                return ["Chưa đủ dữ liệu để đưa dự báo cạn ngân sách."];
            }

            foreach (var budget in budgets)
            {
                forecasts.Add($"Chi phi co dinh da ghi nhan: {forecastSummary.FixedSpentThisMonth:N0} VND.");
                break;
            }

            forecasts.Add($"Chi phi co dinh con lai du kien: {forecastSummary.FixedRemainingThisMonth:N0} VND.");
            forecasts.Add($"Chi tieu linh hoat con lai du kien: {forecastSummary.FlexibleProjectedRemaining:N0} VND.");
            forecasts.Add($"So du cuoi thang uoc tinh: {forecastSummary.ProjectedEndBalance:N0} VND.");

            if (forecasts.Count == 0)
            {
                forecasts.Add("Chưa đủ dữ liệu để đưa dự báo cạn ngân sách.");
            }

            return forecasts;
        }

        private static List<RecurringChargeSignature> IdentifyRecurringCharges(
            IReadOnlyCollection<TransactionEntry> historicalTransactions,
            DateTime today)
        {
            var startHistoryMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-3);
            var priorTransactions = historicalTransactions
                .Where(x => x.Type == "Expense" && x.TransactionDate >= startHistoryMonth)
                .ToList();

            return priorTransactions
                .GroupBy(x => new
                {
                    x.CategoryId,
                    NoteKey = BuildRecurringNoteKey(x.Note),
                    CategoryName = x.Category?.Name ?? "Khac"
                })
                .Select(group =>
                {
                    var ordered = group
                        .OrderByDescending(x => x.TransactionDate)
                        .ToList();
                    var distinctMonths = ordered
                        .Select(x => $"{x.TransactionDate.Year}-{x.TransactionDate.Month}")
                        .Distinct()
                        .ToList();

                    return new
                    {
                        Group = group,
                        Ordered = ordered,
                        DistinctMonths = distinctMonths,
                        MedianAmount = GetMedianAmount(ordered.Select(x => x.Amount)),
                        MedianDay = GetMedianDay(ordered.Select(x => x.TransactionDate.Day))
                    };
                })
                .Where(x => x.DistinctMonths.Count >= 2)
                .Where(x => x.MedianDay <= 10 || IsRecurringByKeyword(x.Group.Key.NoteKey, x.Group.Key.CategoryName))
                .Where(x => x.MedianAmount > 0)
                .Select(x => new RecurringChargeSignature(
                    x.Group.Key.CategoryId,
                    x.Group.Key.CategoryName,
                    x.Group.Key.NoteKey,
                    x.MedianAmount,
                    x.MedianDay))
                .ToList();
        }

        private static List<RecurringChargeMatch> MatchCurrentMonthFixedCharges(
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            IReadOnlyCollection<RecurringChargeSignature> signatures)
        {
            var matches = new List<RecurringChargeMatch>();
            foreach (var signature in signatures)
            {
                var matchedTransaction = monthTransactions
                    .Where(x => x.Type == "Expense" && x.CategoryId == signature.CategoryId)
                    .Where(x => IsSimilarRecurringCharge(x, signature))
                    .OrderBy(x => Math.Abs(x.TransactionDate.Day - (signature.ExpectedDayOfMonth ?? x.TransactionDate.Day)))
                    .ThenBy(x => Math.Abs(x.Amount - signature.ExpectedAmount))
                    .FirstOrDefault();

                var label = BuildRecurringLabel(signature);
                matches.Add(new RecurringChargeMatch(
                    signature,
                    label,
                    signature.ExpectedAmount,
                    signature.ExpectedDayOfMonth,
                    matchedTransaction is not null,
                    matchedTransaction?.TransactionEntryId));
            }

            return matches;
        }

        private static decimal GetMedianAmount(IEnumerable<decimal> amounts)
        {
            var ordered = amounts
                .Where(x => x > 0)
                .OrderBy(x => x)
                .ToList();

            if (ordered.Count == 0)
            {
                return 0;
            }

            var middle = ordered.Count / 2;
            if (ordered.Count % 2 == 1)
            {
                return ordered[middle];
            }

            return (ordered[middle - 1] + ordered[middle]) / 2m;
        }

        private static int? GetMedianDay(IEnumerable<int> days)
        {
            var ordered = days
                .Where(x => x > 0)
                .OrderBy(x => x)
                .ToList();

            if (ordered.Count == 0)
            {
                return null;
            }

            var middle = ordered.Count / 2;
            return ordered.Count % 2 == 1
                ? ordered[middle]
                : (ordered[middle - 1] + ordered[middle]) / 2;
        }

        private static bool IsRecurringByKeyword(string normalizedNote, string categoryName)
        {
            var normalizedCategoryName = NormalizeForecastText(categoryName);
            return normalizedCategoryName.Contains("hoa don", StringComparison.Ordinal) ||
                   normalizedNote.Contains("tro", StringComparison.Ordinal) ||
                   normalizedNote.Contains("tien nha", StringComparison.Ordinal) ||
                   normalizedNote.Contains("thue", StringComparison.Ordinal) ||
                   normalizedNote.Contains("dien", StringComparison.Ordinal) ||
                   normalizedNote.Contains("nuoc", StringComparison.Ordinal) ||
                   normalizedNote.Contains("wifi", StringComparison.Ordinal) ||
                   normalizedNote.Contains("internet", StringComparison.Ordinal);
        }

        private static string BuildRecurringNoteKey(string note)
        {
            var normalized = NormalizeForecastText(note);
            normalized = Regex.Replace(normalized, @"\b\d+\b", string.Empty);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "khong-ghichu" : normalized;
        }

        private static bool IsSimilarRecurringCharge(TransactionEntry transaction, RecurringChargeSignature signature)
        {
            var noteKey = BuildRecurringNoteKey(transaction.Note);
            var amountTolerance = Math.Max(100_000m, signature.ExpectedAmount * 0.35m);
            return noteKey == signature.NoteKey &&
                   Math.Abs(transaction.Amount - signature.ExpectedAmount) <= amountTolerance;
        }

        private static bool IsMatchedAsFixedCharge(
            TransactionEntry transaction,
            IReadOnlyCollection<RecurringChargeMatch> matches)
        {
            return matches.Any(x => x.TransactionId == transaction.TransactionEntryId);
        }

        private static bool MatchesHistoricalFixedSignature(
            TransactionEntry transaction,
            IReadOnlyCollection<RecurringChargeSignature> signatures)
        {
            return signatures.Any(signature =>
                signature.CategoryId == transaction.CategoryId &&
                IsSimilarRecurringCharge(transaction, signature));
        }

        private static decimal CalculateWeightedHistoricalFlexibleDailyAverage(
            IReadOnlyCollection<TransactionEntry> transactions)
        {
            if (transactions.Count == 0)
            {
                return 0m;
            }

            var monthlyDailyAverages = transactions
                .GroupBy(x => new DateTime(x.TransactionDate.Year, x.TransactionDate.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .OrderBy(x => x.Key)
                .Select(group =>
                {
                    var daysInMonth = DateTime.DaysInMonth(group.Key.Year, group.Key.Month);
                    return group.Sum(x => x.Amount) / daysInMonth;
                })
                .ToList();

            if (monthlyDailyAverages.Count == 0)
            {
                return 0m;
            }

            return CalculateWeightedAverage(monthlyDailyAverages);
        }

        private static decimal CalculateWeightedAverage(IReadOnlyList<decimal> values)
        {
            if (values.Count == 0)
            {
                return 0m;
            }

            decimal[] weights = values.Count switch
            {
                1 => [1.0m],
                2 => [0.4m, 0.6m],
                _ => [0.2m, 0.3m, 0.5m]
            };

            var selected = values.Count <= weights.Length
                ? values.ToList()
                : values.Skip(values.Count - weights.Length).ToList();
            if (selected.Count != weights.Length)
            {
                weights = selected.Count switch
                {
                    1 => [1.0m],
                    2 => [0.4m, 0.6m],
                    _ => [0.2m, 0.3m, 0.5m]
                };
            }

            decimal weightedSum = 0m;
            for (var i = 0; i < selected.Count; i++)
            {
                weightedSum += selected[i] * weights[i];
            }

            return decimal.Round(weightedSum, 2);
        }

        private static string BuildRecurringLabel(RecurringChargeSignature signature)
        {
            return string.Equals(signature.NoteKey, "khong-ghichu", StringComparison.Ordinal)
                ? signature.CategoryName
                : $"{signature.CategoryName} - {signature.NoteKey}";
        }

        private static string NormalizeForecastText(string input)
        {
            var normalized = RemoveForecastDiacritics(input ?? string.Empty).ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^a-z0-9\s]", " ");
            return Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        private static string RemoveForecastDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC)
                .Replace('\u0111', 'd')
                .Replace('\u0110', 'D');
        }

        private sealed record RecurringChargeSignature(
            int CategoryId,
            string CategoryName,
            string NoteKey,
            decimal ExpectedAmount,
            int? ExpectedDayOfMonth);

        private sealed record RecurringChargeMatch(
            RecurringChargeSignature Signature,
            string Label,
            decimal ExpectedAmount,
            int? ExpectedDayOfMonth,
            bool AlreadyCharged,
            int? TransactionId);
    }
}
