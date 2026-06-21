namespace SmartSpendAI.Models.Dtos.Dashboard
{
    public class ForecastSummaryDto
    {
        public decimal FixedSpentThisMonth { get; set; }

        public decimal FixedRemainingThisMonth { get; set; }

        public decimal FlexibleSpentThisMonth { get; set; }

        public decimal FlexibleProjectedRemaining { get; set; }

        public decimal ProjectedTotalExpense { get; set; }

        public decimal ProjectedEndBalance { get; set; }

        public decimal CurrentFlexibleDailyAverage { get; set; }

        public decimal HistoricalFlexibleDailyAverage { get; set; }

        public List<string> Explanations { get; set; } = [];

        public List<ForecastBreakdownItemDto> FixedCostItems { get; set; } = [];
    }
}
