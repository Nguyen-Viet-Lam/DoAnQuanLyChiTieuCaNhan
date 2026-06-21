using SmartSpendAI.Models.Dtos.Finance;

namespace SmartSpendAI.Models.Dtos.Dashboard
{
    public class DashboardResponse
    {
        public decimal TotalBalance { get; set; }

        public decimal TotalIncomeThisMonth { get; set; }

        public decimal TotalExpenseThisMonth { get; set; }

        public int UnreadAlerts { get; set; }

        public List<TrendPointDto> MonthlyTrend { get; set; } = [];

        public List<CategoryBreakdownDto> ExpenseBreakdown { get; set; } = [];

        public List<BudgetResponse> BudgetProgress { get; set; } = [];

        public List<string> Insights { get; set; } = [];

        public List<string> Forecasts { get; set; } = [];

        public ForecastSummaryDto? ForecastSummary { get; set; }

        public List<AiReminderResponse> AiReminders { get; set; } = [];
    }
}
