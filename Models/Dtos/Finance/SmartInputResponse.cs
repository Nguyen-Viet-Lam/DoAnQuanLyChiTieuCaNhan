namespace SmartSpendAI.Models.Dtos.Finance
{
    public class SmartInputResponse
    {
        public decimal Amount { get; set; }

        public int? SuggestedCategoryId { get; set; }

        public string SuggestedCategoryName { get; set; } = string.Empty;

        public string SuggestedType { get; set; } = "Expense";

        public int? SuggestedWalletId { get; set; }

        public string SuggestedWalletName { get; set; } = string.Empty;

        public DateTime TransactionDate { get; set; }

        public string NormalizedNote { get; set; } = string.Empty;

        public decimal AiConfidence { get; set; }

        public List<string> MatchedKeywords { get; set; } = [];

        public List<string> Reasoning { get; set; } = [];
    }
}
