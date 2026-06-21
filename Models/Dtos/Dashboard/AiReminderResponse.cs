namespace SmartSpendAI.Models.Dtos.Dashboard
{
    public class AiReminderResponse
    {
        public string Type { get; set; } = string.Empty;

        public string Level { get; set; } = "Info";

        public string Tone { get; set; } = "Friendly";

        public string Message { get; set; } = string.Empty;

        public string? CategoryName { get; set; }

        public decimal? Percentage { get; set; }

        public string? Algorithm { get; set; }

        public string? Explanation { get; set; }

        public string? SuggestedAction { get; set; }

        public decimal? Score { get; set; }

        public decimal? BaselineAmount { get; set; }

        public decimal? CurrentAmount { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
