using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Dashboard;

namespace SmartSpendAI.Services.AI
{
    public class SmartReminderService : ISmartReminderService
    {
        private const int MaxReminders = 10;
        private const decimal NormalExpenseAmount = 500_000m;
        private const decimal MinAnomalySpend = NormalExpenseAmount;
        private const decimal MinRecommendationSpend = NormalExpenseAmount;
        private readonly AppDbContext _dbContext;
        private readonly TimeProvider _timeProvider;

        public SmartReminderService(AppDbContext dbContext, TimeProvider? timeProvider = null)
        {
            _dbContext = dbContext;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<List<AiReminderResponse>> GetMonthlyRemindersAsync(int userId, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var today = now.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            var previousMonthStart = monthStart.AddMonths(-1);
            var currentWeekStart = today.AddDays(-6);
            var currentWeekEnd = today.AddDays(1);
            var previousWeekStart = currentWeekStart.AddDays(-7);
            var oldestHistoryStart = monthStart.AddMonths(-3);
            var oldestWeeklyHistoryStart = currentWeekStart.AddDays(-56);

            var budgets = await _dbContext.Budgets
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.Month == monthStart)
                .ToListAsync(cancellationToken);

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

            var currentWeekTransactions = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.TransactionDate >= currentWeekStart && x.TransactionDate < currentWeekEnd)
                .ToListAsync(cancellationToken);

            var previousWeekTransactions = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.TransactionDate >= previousWeekStart && x.TransactionDate < currentWeekStart)
                .ToListAsync(cancellationToken);

            var historicalExpenses = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId &&
                            x.Type == "Expense" &&
                            x.TransactionDate >= oldestHistoryStart &&
                            x.TransactionDate < monthStart)
                .ToListAsync(cancellationToken);

            var weeklyHistoryExpenses = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId &&
                            x.Type == "Expense" &&
                            x.TransactionDate >= oldestWeeklyHistoryStart &&
                            x.TransactionDate < currentWeekStart)
                .ToListAsync(cancellationToken);

            var reminders = new List<ReminderCandidate>();
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddReminder(
                string key,
                string type,
                string level,
                string message,
                string? categoryName = null,
                decimal? percentage = null,
                int priority = 100,
                string tone = "Friendly",
                string? algorithm = null,
                string? explanation = null,
                string? suggestedAction = null,
                decimal? score = null,
                decimal? baselineAmount = null,
                decimal? currentAmount = null)
            {
                if (!dedupe.Add(key))
                {
                    return;
                }

                reminders.Add(new ReminderCandidate(
                    priority,
                    new AiReminderResponse
                    {
                        Type = type,
                        Level = level,
                        Tone = tone,
                        Message = message,
                        CategoryName = categoryName,
                        Percentage = percentage,
                        Algorithm = algorithm,
                        Explanation = explanation,
                        SuggestedAction = suggestedAction,
                        Score = score,
                        BaselineAmount = baselineAmount,
                        CurrentAmount = currentAmount,
                        CreatedAt = now
                    }));
            }

            AddBudgetReminders(budgets, monthTransactions, today, monthEnd, AddReminder);
            AddTrendReminders(monthTransactions, previousMonthTransactions, currentWeekTransactions, previousWeekTransactions, AddReminder);
            AddWeightedForecastReminder(budgets, monthTransactions, historicalExpenses, today, monthEnd, AddReminder);
            AddAnomalyReminders(currentWeekTransactions, weeklyHistoryExpenses, currentWeekStart, AddReminder);
            AddTopRecommendations(budgets, monthTransactions, historicalExpenses, today, AddReminder);
            AddLargeExpenseReminder(historicalExpenses, monthTransactions, AddReminder);
            AddInactiveReminder(monthTransactions, today, AddReminder);
            AddOverallBudgetPaceReminder(budgets, monthTransactions, today, monthEnd, AddReminder);

            return reminders
                .OrderBy(x => x.Priority)
                .ThenByDescending(x => x.Reminder.Score ?? 0m)
                .ThenBy(x => x.Reminder.CategoryName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Reminder)
                .Take(MaxReminders)
                .ToList();
        }

        private static void AddBudgetReminders(
            IReadOnlyCollection<Budget> budgets,
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            DateTime today,
            DateTime monthEnd,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var daysRemaining = Math.Max(0, (monthEnd.Date - today).Days);

            foreach (var budget in budgets)
            {
                var spent = monthTransactions
                    .Where(x => x.Type == "Expense" && x.CategoryId == budget.CategoryId)
                    .Sum(x => x.Amount);

                if (budget.LimitAmount <= 0)
                {
                    continue;
                }

                var percentage = decimal.Round(spent / budget.LimitAmount * 100m, 2);
                var categoryName = budget.Category?.Name ?? "Khac";

                if (percentage >= 100m)
                {
                    addReminder(
                        $"budget-danger:{budget.CategoryId}",
                        "Budget",
                        "Danger",
                        $"Ngan sach {categoryName} da vuot muc ({percentage:0}%).",
                        categoryName,
                        percentage,
                        0,
                        "Serious",
                        "BudgetThreshold",
                        $"Da chi {FormatMoney(spent)} tren gioi han {FormatMoney(budget.LimitAmount)}.",
                        $"Tam thoi han che chi tieu moi trong nhom {categoryName}.",
                        percentage / 100m,
                        budget.LimitAmount,
                        spent);
                    continue;
                }

                if (percentage >= 80m)
                {
                    addReminder(
                        $"budget-warning:{budget.CategoryId}",
                        "Budget",
                        "Warning",
                        $"Ngan sach {categoryName} da dung {percentage:0}% han muc.",
                        categoryName,
                        percentage,
                        1,
                        "Friendly",
                        "BudgetThreshold",
                        $"Da chi {FormatMoney(spent)} / {FormatMoney(budget.LimitAmount)}.",
                        $"Theo doi sat nhom {categoryName} trong {daysRemaining} ngay con lai.",
                        percentage / 100m,
                        budget.LimitAmount,
                        spent);
                    continue;
                }

                if (daysRemaining <= 7 && percentage >= 60m)
                {
                    addReminder(
                        $"budget-close:{budget.CategoryId}",
                        "Budget",
                        "Info",
                        $"Nhóm {categoryName} đang tiến sát ngân sách cuối tháng.",
                        categoryName,
                        percentage,
                        2,
                        "Friendly",
                        "BudgetThreshold",
                        $"Con {daysRemaining} ngay, nhom nay da dung {percentage:0}% han muc.",
                        $"Giu toc do chi tieu nhom {categoryName} o muc on dinh.",
                        percentage / 100m,
                        budget.LimitAmount,
                        spent);
                }
            }
        }

        private static void AddTrendReminders(
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            IReadOnlyCollection<TransactionEntry> previousMonthTransactions,
            IReadOnlyCollection<TransactionEntry> currentWeekTransactions,
            IReadOnlyCollection<TransactionEntry> previousWeekTransactions,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var currentFood = monthTransactions
                .Where(x => x.Type == "Expense" && IsFoodCategory(x.Category?.Name))
                .Sum(x => x.Amount);
            var previousFood = previousMonthTransactions
                .Where(x => x.Type == "Expense" && IsFoodCategory(x.Category?.Name))
                .Sum(x => x.Amount);

            if (currentFood >= 200_000m && (previousFood <= 0 || currentFood >= previousFood * 1.25m))
            {
                var percentage = previousFood > 0
                    ? decimal.Round((currentFood - previousFood) / previousFood * 100m, 1)
                    : 100m;
                addReminder(
                    "food-trend",
                    "Trend",
                    "Warning",
                    "Chi tiêu ăn uống tháng này đang tăng nhanh hơn thói quen gần đây.",
                    "An uong",
                    percentage,
                    1,
                    "Friendly",
                    "MonthlyTrend",
                    $"Thang nay: {FormatMoney(currentFood)}. Thang truoc: {FormatMoney(previousFood)}.",
                    "Can doi lai tan suat an ngoai hoac dat tran nho cho nhom an uong.",
                    previousFood > 0 ? currentFood / previousFood : 1.5m,
                    previousFood,
                    currentFood);
            }

            var previousWeekByCategory = previousWeekTransactions
                .Where(x => x.Type == "Expense")
                .GroupBy(x => x.Category?.Name ?? "Khac")
                .ToDictionary(group => group.Key, group => group.Sum(x => x.Amount), StringComparer.OrdinalIgnoreCase);

            var topIncrease = currentWeekTransactions
                .Where(x => x.Type == "Expense")
                .GroupBy(x => x.Category?.Name ?? "Khac")
                .Select(group =>
                {
                    previousWeekByCategory.TryGetValue(group.Key, out var previousAmount);
                    var currentAmount = group.Sum(x => x.Amount);
                    return new
                    {
                        CategoryName = group.Key,
                        CurrentAmount = currentAmount,
                        PreviousAmount = previousAmount,
                        Delta = currentAmount - previousAmount
                    };
                })
                .OrderByDescending(x => x.Delta)
                .FirstOrDefault();

            if (topIncrease is not null &&
                topIncrease.Delta > 0 &&
                topIncrease.CurrentAmount >= 100_000m &&
                (topIncrease.PreviousAmount <= 0 || topIncrease.CurrentAmount >= topIncrease.PreviousAmount * 1.2m))
            {
                var percentage = topIncrease.PreviousAmount > 0
                    ? decimal.Round((topIncrease.CurrentAmount - topIncrease.PreviousAmount) / topIncrease.PreviousAmount * 100m, 1)
                    : 100m;
                addReminder(
                    $"week-increase:{NormalizeKey(topIncrease.CategoryName)}",
                    "Trend",
                    "Info",
                    $"Tuan nay nhom {topIncrease.CategoryName} dang chi nhanh hon tuan truoc.",
                    topIncrease.CategoryName,
                    percentage,
                    3,
                    "Friendly",
                    "WeeklyTrend",
                    $"Tuan nay: {FormatMoney(topIncrease.CurrentAmount)}. Tuan truoc: {FormatMoney(topIncrease.PreviousAmount)}.",
                    $"Nếu chưa cần thiết, giảm bớt chi nhanh trong nhóm {topIncrease.CategoryName}.",
                    topIncrease.PreviousAmount > 0 ? topIncrease.CurrentAmount / topIncrease.PreviousAmount : 1.2m,
                    topIncrease.PreviousAmount,
                    topIncrease.CurrentAmount);
            }
        }

        private static void AddWeightedForecastReminder(
            IReadOnlyCollection<Budget> budgets,
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            IReadOnlyCollection<TransactionEntry> historicalExpenses,
            DateTime today,
            DateTime monthEnd,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var totalLimit = budgets.Sum(x => x.LimitAmount);
            if (totalLimit <= 0)
            {
                return;
            }

            var currentExpense = monthTransactions
                .Where(x => x.Type == "Expense")
                .Sum(x => x.Amount);

            var elapsedDays = Math.Max(1, today.Day);
            var daysRemaining = Math.Max(0, (monthEnd.Date - today).Days);
            var currentDailyAverage = currentExpense / elapsedDays;
            var historicalWeightedDailyAverage = CalculateWeightedDailyAverageFromHistory(historicalExpenses);
            var blendedDailyAverage = BlendCurrentWithHistory(currentDailyAverage, historicalWeightedDailyAverage);
            var projectedTotalExpense = decimal.Round(currentExpense + (blendedDailyAverage * daysRemaining), 0);

            if (projectedTotalExpense < totalLimit * 1.05m)
            {
                return;
            }

            var percentage = decimal.Round(projectedTotalExpense / totalLimit * 100m, 1);
            addReminder(
                "forecast-wma",
                "Forecast",
                projectedTotalExpense >= totalLimit * 1.15m ? "Danger" : "Warning",
                "Nếu giữ nhịp hiện tại, tổng chi tiêu cuối tháng có thể vượt ngân sách chung.",
                null,
                percentage,
                1,
                "Serious",
                "WeightedMovingAverage",
                $"Du bao dung weighted moving average: hien tai {FormatMoney(currentDailyAverage)}/ngay, lich su co trong so {FormatMoney(historicalWeightedDailyAverage)}/ngay.",
                "Can doi lai cac nhom chi linh hoat trong phan con lai cua thang.",
                totalLimit > 0 ? projectedTotalExpense / totalLimit : null,
                totalLimit,
                projectedTotalExpense);
        }

        private static void AddAnomalyReminders(
            IReadOnlyCollection<TransactionEntry> currentWeekTransactions,
            IReadOnlyCollection<TransactionEntry> weeklyHistoryExpenses,
            DateTime currentWeekStart,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var currentByCategory = currentWeekTransactions
                .Where(x => x.Type == "Expense")
                .GroupBy(x => new { x.CategoryId, CategoryName = x.Category?.Name ?? "Khac" })
                .Select(group => new
                {
                    group.Key.CategoryId,
                    group.Key.CategoryName,
                    CurrentAmount = group.Sum(x => x.Amount)
                })
                .Where(x => x.CurrentAmount >= MinAnomalySpend)
                .ToList();

            if (currentByCategory.Count == 0)
            {
                return;
            }

            var weeklySnapshots = weeklyHistoryExpenses
                .GroupBy(x => new
                {
                    x.CategoryId,
                    CategoryName = x.Category?.Name ?? "Khac",
                    WeekStart = StartOfSevenDayWindow(x.TransactionDate.Date, currentWeekStart)
                })
                .Select(group => new
                {
                    group.Key.CategoryId,
                    group.Key.CategoryName,
                    group.Key.WeekStart,
                    Amount = group.Sum(x => x.Amount)
                })
                .ToList();

            foreach (var current in currentByCategory)
            {
                var samples = weeklySnapshots
                    .Where(x => x.CategoryId == current.CategoryId)
                    .OrderByDescending(x => x.WeekStart)
                    .Select(x => x.Amount)
                    .Take(8)
                    .ToList();

                if (samples.Count < 2)
                {
                    continue;
                }

                var mean = samples.Average();
                var stdDeviation = CalculateStandardDeviation(samples, mean);
                var threshold = Math.Max(mean * 1.5m, mean + (stdDeviation * 1.5m));
                threshold = Math.Max(threshold, NormalExpenseAmount);

                if (mean <= 0 || current.CurrentAmount < Math.Max(threshold, MinAnomalySpend))
                {
                    continue;
                }

                var percentage = decimal.Round((current.CurrentAmount - mean) / mean * 100m, 1);
                addReminder(
                    $"anomaly:{current.CategoryId}",
                    "Anomaly",
                    current.CurrentAmount >= threshold * 1.25m ? "Danger" : "Warning",
                    $"Tuan nay nhom {current.CategoryName} co dau hieu bat thuong so voi muc quen thuoc.",
                    current.CategoryName,
                    percentage,
                    1,
                    "Serious",
                    "StatisticalAnomalyDetection",
                    $"Tuan nay: {FormatMoney(current.CurrentAmount)}. Trung binh lich su: {FormatMoney(mean)}. Nguong canh bao: {FormatMoney(threshold)}.",
                    $"Kiểm tra các khoản chi lớn trong nhóm {current.CategoryName} và xác nhận xem có phát sinh đột biến không.",
                    mean > 0 ? current.CurrentAmount / mean : null,
                    mean,
                    current.CurrentAmount);
            }
        }

        private static void AddTopRecommendations(
            IReadOnlyCollection<Budget> budgets,
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            IReadOnlyCollection<TransactionEntry> historicalExpenses,
            DateTime today,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var budgetByCategory = budgets
                .Where(x => x.LimitAmount > 0)
                .ToDictionary(x => x.CategoryId, x => x, EqualityComparer<int>.Default);

            var historicalMonthlyAverageByCategory = historicalExpenses
                .GroupBy(x => new { x.CategoryId, Month = new DateTime(x.TransactionDate.Year, x.TransactionDate.Month, 1, 0, 0, 0, DateTimeKind.Utc) })
                .GroupBy(x => x.Key.CategoryId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.Sum(x => x.Amount)).DefaultIfEmpty(0m).Average());

            var elapsedDays = Math.Max(1, today.Day);

            var recommendations = monthTransactions
                .Where(x => x.Type == "Expense")
                .GroupBy(x => new { x.CategoryId, CategoryName = x.Category?.Name ?? "Khac" })
                .Select(group =>
                {
                    var spent = group.Sum(x => x.Amount);
                    historicalMonthlyAverageByCategory.TryGetValue(group.Key.CategoryId, out var historicalAverage);
                    budgetByCategory.TryGetValue(group.Key.CategoryId, out var budget);

                    var budgetOverrunRatio = budget is not null && budget.LimitAmount > 0
                        ? Math.Max(0m, (spent / budget.LimitAmount) - 1m)
                        : 0m;
                    var growthRatio = historicalAverage > 0
                        ? Math.Max(0m, (spent / historicalAverage) - 1m)
                        : spent >= MinRecommendationSpend ? 0.20m : 0m;
                    var currentDailyAverage = spent / elapsedDays;
                    var historicalDailyAverage = historicalAverage > 0
                        ? historicalAverage / DateTime.DaysInMonth(today.Year, today.Month)
                        : 0m;
                    var paceRatio = historicalDailyAverage > 0
                        ? Math.Max(0m, (currentDailyAverage / historicalDailyAverage) - 1m)
                        : 0m;
                    var score = (budgetOverrunRatio * 0.55m) + (growthRatio * 0.30m) + (paceRatio * 0.15m);

                    return new
                    {
                        group.Key.CategoryId,
                        group.Key.CategoryName,
                        Spent = spent,
                        HistoricalAverage = historicalAverage,
                        BudgetLimit = budget?.LimitAmount ?? 0m,
                        Score = decimal.Round(score, 3),
                        BudgetOverrunRatio = budgetOverrunRatio,
                        GrowthRatio = growthRatio
                    };
                })
                .Where(x => x.Spent >= MinRecommendationSpend)
                .Where(x => x.Score >= 0.20m || x.BudgetOverrunRatio > 0m || x.GrowthRatio >= 0.30m)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Spent)
                .Take(3)
                .ToList();

            for (var index = 0; index < recommendations.Count; index++)
            {
                var item = recommendations[index];
                var baseline = item.BudgetLimit > 0m ? item.BudgetLimit : item.HistoricalAverage;
                decimal? percentage = baseline > 0
                    ? decimal.Round(item.Spent / baseline * 100m, 1)
                    : null;
                addReminder(
                    $"recommend:{item.CategoryId}",
                    "Recommendation",
                    item.BudgetOverrunRatio > 0m ? "Warning" : "Info",
                    $"Goi y uu tien siet nhom {item.CategoryName} de ha ap luc chi tieu.",
                    item.CategoryName,
                    percentage,
                    2 + index,
                    "Friendly",
                    "TopKRecommendation",
                    BuildRecommendationExplanation(item.CategoryName, item.Spent, item.HistoricalAverage, item.BudgetLimit, item.Score),
                    BuildRecommendationAction(item.CategoryName, item.BudgetOverrunRatio, item.GrowthRatio),
                    item.Score,
                    baseline,
                    item.Spent);
            }
        }

        private static string BuildRecommendationExplanation(
            string categoryName,
            decimal spent,
            decimal historicalAverage,
            decimal budgetLimit,
            decimal score)
        {
            if (budgetLimit > 0)
            {
                return $"Nhóm {categoryName} đang chi {FormatMoney(spent)} trên ngân sách {FormatMoney(budgetLimit)}. Recommendation score: {score:0.00}.";
            }

            if (historicalAverage > 0)
            {
                return $"Nhóm {categoryName} đang chi {FormatMoney(spent)} so với mức lịch sử {FormatMoney(historicalAverage)}. Recommendation score: {score:0.00}.";
            }

            return $"Nhóm {categoryName} đang có mức chi cao và tốc độ tăng nhanh. Recommendation score: {score:0.00}.";
        }

        private static string BuildRecommendationAction(string categoryName, decimal budgetOverrunRatio, decimal growthRatio)
        {
            if (budgetOverrunRatio > 0.10m)
            {
                return $"Đặt trần tạm thời và trì hoãn các khoản chi mới trong nhóm {categoryName}.";
            }

            if (growthRatio >= 0.50m)
            {
                return $"Soát lại các khoản phát sinh mới trong nhóm {categoryName} vì đang tăng nhanh hơn thói quen.";
            }

            return $"Theo dõi sát nhóm {categoryName} trong 7 ngày tới để giữ forecast ổn định.";
        }

        private static void AddLargeExpenseReminder(
            IReadOnlyCollection<TransactionEntry> historicalExpenses,
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var largestExpense = monthTransactions
                .Where(x => x.Type == "Expense")
                .OrderByDescending(x => x.Amount)
                .FirstOrDefault();

            if (largestExpense is null)
            {
                return;
            }

            var historicalCount = historicalExpenses.Count;
            if (historicalCount < 5)
            {
                return;
            }

            var averageHistoricalAmount = historicalExpenses
                .Where(x => x.Amount > 0)
                .Select(x => x.Amount)
                .DefaultIfEmpty()
                .Average();

            if (averageHistoricalAmount <= 0)
            {
                return;
            }

            if (largestExpense.Amount < Math.Max(NormalExpenseAmount, averageHistoricalAmount * 2.5m))
            {
                return;
            }

            addReminder(
                $"large-expense:{largestExpense.TransactionEntryId}",
                "Spike",
                "Warning",
                $"Khoan chi {FormatMoney(largestExpense.Amount)} o {largestExpense.Category?.Name ?? "Khac"} lon hon muc quen thuoc.",
                largestExpense.Category?.Name,
                null,
                2,
                "Serious",
                "OutlierSpike",
                $"Gia tri giao dich cao hon muc trung binh lich su {FormatMoney(averageHistoricalAmount)}.",
                "Kiem tra lai giao dich lon nay va cap nhat ghi chu cho ro hon neu can.",
                largestExpense.Amount / averageHistoricalAmount,
                averageHistoricalAmount,
                largestExpense.Amount);
        }

        private static void AddInactiveReminder(
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            DateTime today,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var lastTransaction = monthTransactions
                .OrderByDescending(x => x.TransactionDate)
                .FirstOrDefault();

            if (lastTransaction is null)
            {
                addReminder(
                    "inactive:none",
                    "Activity",
                    "Info",
                    "Tháng này bạn chưa có giao dịch nào, AI sẽ dự đoán tốt hơn khi có thêm dữ liệu.",
                    null,
                    null,
                    5,
                    "Friendly",
                    "ActivityGap",
                    "Chưa có dữ liệu giao dịch trong tháng hiện tại.",
                    "Nhập đều các giao dịch thu chi để dashboard học thói quen sát hơn.",
                    null,
                    null,
                    null);
                return;
            }

            var gapDays = (today - lastTransaction.TransactionDate.Date).Days;
            if (gapDays >= 5)
            {
                addReminder(
                    $"inactive:{lastTransaction.TransactionDate:yyyyMMdd}",
                    "Activity",
                    "Info",
                    $"Đã {gapDays} ngày bạn chưa thêm giao dịch mới.",
                    null,
                    null,
                    5,
                    "Friendly",
                    "ActivityGap",
                    $"Lan cap nhat gan nhat la ngay {lastTransaction.TransactionDate:dd/MM/yyyy}.",
                    "Nhập giao dịch sớm hơn để reminder và forecast chính xác hơn.",
                    gapDays,
                    null,
                    null);
            }
        }

        private static void AddOverallBudgetPaceReminder(
            IReadOnlyCollection<Budget> budgets,
            IReadOnlyCollection<TransactionEntry> monthTransactions,
            DateTime today,
            DateTime monthEnd,
            Action<string, string, string, string, string?, decimal?, int, string, string?, string?, string?, decimal?, decimal?, decimal?> addReminder)
        {
            var totalLimit = budgets.Sum(x => x.LimitAmount);
            if (totalLimit <= 0)
            {
                return;
            }

            var totalSpent = monthTransactions
                .Where(x => x.Type == "Expense")
                .Sum(x => x.Amount);

            var daysRemaining = Math.Max(0, (monthEnd.Date - today).Days);
            var spendingRatio = totalSpent / totalLimit * 100m;

            if (spendingRatio >= 80m && daysRemaining <= 10)
            {
                addReminder(
                    "overall-pace",
                    "Budget",
                    spendingRatio >= 100m ? "Danger" : "Warning",
                    spendingRatio >= 100m
                        ? "Tổng ngân sách tháng này đã chạm giới hạn."
                        : "Tổng ngân sách tháng này đang mỏng hơn tốc độ chi hiện tại.",
                    null,
                    decimal.Round(spendingRatio, 2),
                    1,
                    spendingRatio >= 100m ? "Serious" : "Friendly",
                    "BudgetPace",
                    $"Da chi {FormatMoney(totalSpent)} / {FormatMoney(totalLimit)} va con {daysRemaining} ngay.",
                    "Giam toc o cac nhom chi linh hoat trong phan con lai cua thang.",
                    spendingRatio / 100m,
                    totalLimit,
                    totalSpent);
            }
        }

        private static decimal CalculateWeightedDailyAverageFromHistory(IReadOnlyCollection<TransactionEntry> transactions)
        {
            var monthlyDailyAverages = transactions
                .GroupBy(x => new DateTime(x.TransactionDate.Year, x.TransactionDate.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .OrderBy(group => group.Key)
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

        private static decimal BlendCurrentWithHistory(decimal currentDailyAverage, decimal historicalWeightedDailyAverage)
        {
            if (currentDailyAverage > 0m && historicalWeightedDailyAverage > 0m)
            {
                return decimal.Round((currentDailyAverage * 0.55m) + (historicalWeightedDailyAverage * 0.45m), 2);
            }

            return decimal.Round(Math.Max(currentDailyAverage, historicalWeightedDailyAverage), 2);
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
                ? values
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

        private static DateTime StartOfSevenDayWindow(DateTime transactionDate, DateTime currentWeekStart)
        {
            var daysDifference = (currentWeekStart - transactionDate.Date).Days;
            var bucketIndex = Math.Max(1, (int)Math.Ceiling(daysDifference / 7d));
            return currentWeekStart.AddDays(-7 * bucketIndex);
        }

        private static decimal CalculateStandardDeviation(IReadOnlyCollection<decimal> samples, decimal mean)
        {
            if (samples.Count == 0)
            {
                return 0m;
            }

            var variance = samples
                .Select(sample => (double)((sample - mean) * (sample - mean)))
                .Average();

            return (decimal)Math.Sqrt(variance);
        }

        private static string FormatMoney(decimal amount)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0} VND", decimal.Round(amount, 0));
        }

        private static bool IsFoodCategory(string? categoryName)
        {
            var normalized = NormalizeText(categoryName ?? string.Empty);
            return normalized.Contains("an uong", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("food", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("restaurant", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKey(string value)
        {
            return Regex.Replace(NormalizeText(value), @"[^a-z0-9]+", "-");
        }

        private static string NormalizeText(string input)
        {
            var text = RemoveDiacritics(input ?? string.Empty).ToLowerInvariant();
            text = Regex.Replace(text, @"[^a-z0-9\s]", " ");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC)
                .Replace('\u0111', 'd')
                .Replace('\u0110', 'D');
        }

        private sealed record ReminderCandidate(int Priority, AiReminderResponse Reminder);
    }
}
