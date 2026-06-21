namespace SmartSpendAI.Models.Dtos.Dashboard
{
    public class ForecastBreakdownItemDto
    {
        public string Label { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public bool AlreadyCharged { get; set; }

        public int? ExpectedDayOfMonth { get; set; }
    }
}
