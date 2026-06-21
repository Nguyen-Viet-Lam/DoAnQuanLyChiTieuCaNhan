using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Finance;

namespace SmartSpendAI.Services.AI
{
    public class SmartInputService : ISmartInputService
    {
        private readonly AppDbContext _dbContext;
        private const int HistoryTransactionLimit = 150;
        private static readonly HashSet<string> NoiseKeywords =
        [
            "chi",
            "thu",
            "mua",
            "tra",
            "cho",
            "va",
            "tu",
            "den",
            "hom",
            "nay",
            "qua",
            "tuan",
            "thang",
            "nam"
        ];

        public SmartInputService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<SmartInputResponse> ParseAsync(string input, int userId, CancellationToken cancellationToken)
        {
            var normalized = Normalize(input);
            var amount = ExtractAmount(normalized);
            var transactionDate = ExtractDate(normalized);

            var matchedKeywords = new List<string>();
            var reasoning = new List<string>();
            Category? category = null;
            Wallet? wallet = null;
            var usedPersonalKeyword = false;
            var usedHistoryInference = false;

            var historicalTransactions = await _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Include(x => x.Wallet)
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.TransactionDate)
                .ThenByDescending(x => x.TransactionEntryId)
                .Take(HistoryTransactionLimit)
                .ToListAsync(cancellationToken);

            var personalKeywords = await _dbContext.UserPersonalKeywords
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.UsageCount)
                .ThenByDescending(x => x.Keyword.Length)
                .ToListAsync(cancellationToken);

            foreach (var personalKeyword in personalKeywords)
            {
                var normalizedKeyword = Normalize(personalKeyword.Keyword);
                if (string.IsNullOrWhiteSpace(normalizedKeyword))
                {
                    continue;
                }

                if (!normalized.Contains(normalizedKeyword, StringComparison.Ordinal))
                {
                    continue;
                }

                matchedKeywords.Add(personalKeyword.Keyword);
                category = personalKeyword.Category;
                usedPersonalKeyword = true;
                reasoning.Add($"Học từ lịch sử sửa tay: \"{personalKeyword.Keyword}\"");
                break;
            }

            if (!usedPersonalKeyword)
            {
                var keywords = await _dbContext.Keywords
                    .AsNoTracking()
                    .Include(x => x.Category)
                    .Where(x => x.IsActive)
                    .ToListAsync(cancellationToken);

                var bestScore = 0;
                foreach (var keyword in keywords)
                {
                    var normalizedKeyword = Normalize(keyword.Word);
                    if (!normalized.Contains(normalizedKeyword, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matchedKeywords.Add(keyword.Word);
                    if (keyword.Weight > bestScore)
                    {
                        bestScore = keyword.Weight;
                        category = keyword.Category;
                    }
                }

                if (category is not null && matchedKeywords.Count > 0)
                {
                    reasoning.Add($"Nhận diện từ khóa hệ thống: {string.Join(", ", matchedKeywords.Distinct(StringComparer.OrdinalIgnoreCase))}");
                }
            }

            var historySuggestion = SuggestFromHistory(normalized, amount, historicalTransactions);
            if (historySuggestion is not null)
            {
                if (category is null)
                {
                    category = historySuggestion.Category;
                    usedHistoryInference = true;
                }

                wallet = historySuggestion.Wallet;

                foreach (var reason in historySuggestion.Reasons)
                {
                    if (!reasoning.Any(item => string.Equals(item, reason, StringComparison.OrdinalIgnoreCase)))
                    {
                        reasoning.Add(reason);
                    }
                }

                foreach (var keyword in historySuggestion.MatchedTokens)
                {
                    if (!matchedKeywords.Any(item => string.Equals(item, keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedKeywords.Add(keyword);
                    }
                }
            }

            if (wallet is null && category is not null)
            {
                wallet = historicalTransactions
                    .Where(x => x.CategoryId == category.CategoryId)
                    .GroupBy(x => new { x.WalletId, WalletName = x.Wallet.Name })
                    .OrderByDescending(group => group.Count())
                    .ThenByDescending(group => group.Max(item => item.TransactionDate))
                    .Select(group => new Wallet
                    {
                        WalletId = group.Key.WalletId,
                        Name = group.Key.WalletName
                    })
                    .FirstOrDefault();

                if (wallet is not null)
                {
                    reasoning.Add($"Gợi ý ví thường dùng cho danh mục {category.Name}: {wallet.Name}");
                }
            }

            if (wallet is null)
            {
                wallet = await _dbContext.Wallets
                    .AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (wallet is not null)
                {
                    reasoning.Add($"Mặc định ví theo ưu tiên hiện tại: {wallet.Name}");
                }
            }

            var suggestedType = category?.Type ?? InferTransactionType(normalized, amount);

            var confidence = 0.20m;
            if (amount > 0)
            {
                confidence += 0.30m;
            }

            if (category is not null)
            {
                confidence += usedPersonalKeyword ? 0.40m : usedHistoryInference ? 0.32m : 0.25m;
            }

            if (wallet is not null)
            {
                confidence += 0.10m;
            }

            if (!transactionDate.Date.Equals(DateTime.UtcNow.Date))
            {
                confidence += 0.08m;
            }

            confidence += Math.Min(0.18m, matchedKeywords.Count * 0.03m);

            return new SmartInputResponse
            {
                Amount = amount,
                SuggestedCategoryId = category?.CategoryId,
                SuggestedCategoryName = category?.Name ?? string.Empty,
                SuggestedType = suggestedType,
                SuggestedWalletId = wallet?.WalletId,
                SuggestedWalletName = wallet?.Name ?? string.Empty,
                TransactionDate = transactionDate,
                NormalizedNote = BuildNormalizedNote(input),
                AiConfidence = Math.Min(0.99m, confidence),
                MatchedKeywords = matchedKeywords,
                Reasoning = reasoning
            };
        }

        public async Task LearnFromCorrectionAsync(string input, int userId, int correctedCategoryId, CancellationToken cancellationToken)
        {
            var categoryExists = await _dbContext.Categories
                .AsNoTracking()
                .AnyAsync(x => x.CategoryId == correctedCategoryId, cancellationToken);

            if (!categoryExists)
            {
                throw new InvalidOperationException("Danh mục không tồn tại.");
            }

            var normalizedInput = Normalize(input);
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return;
            }

            var learningKeywords = ExtractLearningKeywords(normalizedInput);
            if (learningKeywords.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var keyword in learningKeywords)
            {
                var existing = await _dbContext.UserPersonalKeywords
                    .FirstOrDefaultAsync(
                        x => x.UserId == userId && x.Keyword == keyword,
                        cancellationToken);

                if (existing is null)
                {
                    _dbContext.UserPersonalKeywords.Add(new UserPersonalKeyword
                    {
                        UserId = userId,
                        CategoryId = correctedCategoryId,
                        Keyword = keyword,
                        UsageCount = 1,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else
                {
                    existing.CategoryId = correctedCategoryId;
                    existing.UsageCount += 1;
                    existing.UpdatedAt = now;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static List<string> ExtractLearningKeywords(string normalizedInput)
        {
            var keywords = new HashSet<string>(StringComparer.Ordinal);
            var compact = Regex.Replace(normalizedInput, "\\s+", " ").Trim();
            if (compact.Length >= 3)
            {
                keywords.Add(compact);
            }

            foreach (var token in compact.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length < 3 || NoiseKeywords.Contains(token))
                {
                    continue;
                }

                if (decimal.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                keywords.Add(token);
            }

            return keywords.Take(8).ToList();
        }

        private static string BuildNormalizedNote(string input)
        {
            return Regex.Replace(input.Trim(), "\\s+", " ");
        }

        private static HistorySuggestion? SuggestFromHistory(
            string normalizedInput,
            decimal amount,
            IReadOnlyCollection<TransactionEntry> transactions)
        {
            var inputTokens = ExtractMeaningfulTokens(normalizedInput);
            if (inputTokens.Count == 0)
            {
                return null;
            }

            var candidates = transactions
                .Where(x => x.Category is not null && x.Wallet is not null)
                .Select(transaction =>
                {
                    var noteTokens = ExtractMeaningfulTokens(Normalize(transaction.Note));
                    var overlap = inputTokens.Intersect(noteTokens, StringComparer.Ordinal).ToList();
                    if (overlap.Count == 0)
                    {
                        return null;
                    }

                    var overlapScore = overlap.Count * 3m;
                    var amountScore = amount > 0 && transaction.Amount > 0
                        ? 1m - Math.Min(1m, Math.Abs(transaction.Amount - amount) / Math.Max(transaction.Amount, amount))
                        : 0m;
                    var recencyBoost = transaction.TransactionDate >= DateTime.UtcNow.AddMonths(-1) ? 1.5m : 0m;
                    var totalScore = overlapScore + amountScore + recencyBoost;

                    return new
                    {
                        Transaction = transaction,
                        Score = totalScore,
                        Overlap = overlap
                    };
                })
                .Where(x => x is not null)
                .OrderByDescending(x => x!.Score)
                .ThenByDescending(x => x!.Transaction.TransactionDate)
                .Take(5)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var best = candidates[0]!;
            if (best.Score < 3m)
            {
                return null;
            }

            var reasons = new List<string>
            {
                $"Gần với lịch sử giao dịch: \"{best.Transaction.Note}\""
            };

            if (best.Transaction.Wallet is not null)
            {
                reasons.Add($"Ví gần giống nhất trong lịch sử: {best.Transaction.Wallet.Name}");
            }

            return new HistorySuggestion(
                best.Transaction.Category,
                best.Transaction.Wallet,
                reasons,
                best.Overlap);
        }

        private static List<string> ExtractMeaningfulTokens(string normalizedInput)
        {
            return normalizedInput
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 3 && !NoiseKeywords.Contains(token))
                .Where(token => !decimal.TryParse(token.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .Distinct(StringComparer.Ordinal)
                .Take(10)
                .ToList();
        }

        private static string InferTransactionType(string normalizedInput, decimal amount)
        {
            if (normalizedInput.Contains("luong", StringComparison.Ordinal) ||
                normalizedInput.Contains("thuong", StringComparison.Ordinal) ||
                normalizedInput.Contains("nhan tien", StringComparison.Ordinal) ||
                normalizedInput.Contains("thu no", StringComparison.Ordinal))
            {
                return "Income";
            }

            return amount >= 0 ? "Expense" : "Income";
        }

        private static decimal ExtractAmount(string normalized)
        {
            var millionMatch = Regex.Match(normalized, @"(?<!\d)(\d+)\s*tr(\d{1,3})?(?!\d)");
            if (millionMatch.Success)
            {
                var leading = decimal.Parse(millionMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 1_000_000m;
                var suffix = millionMatch.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    var suffixValue = decimal.Parse(suffix, CultureInfo.InvariantCulture);
                    var multiplier = (decimal)Math.Pow(10, 6 - suffix.Length);
                    leading += suffixValue * multiplier;
                }

                return leading;
            }

            var kiloMatch = Regex.Match(normalized, @"(?<!\d)(\d+(?:[.,]\d+)?)\s*k(?!\w)");
            if (kiloMatch.Success)
            {
                var raw = kiloMatch.Groups[1].Value.Replace(",", ".");
                return decimal.Parse(raw, CultureInfo.InvariantCulture) * 1_000m;
            }

            var separatorMatch = Regex.Match(normalized, @"(?<!\d)(\d{1,3}(?:[.,]\d{3})+)(?!\d)");
            if (separatorMatch.Success)
            {
                var cleaned = separatorMatch.Groups[1].Value.Replace(".", string.Empty).Replace(",", string.Empty);
                return decimal.Parse(cleaned, CultureInfo.InvariantCulture);
            }

            var plainMatch = Regex.Match(normalized, @"(?<!\d)(\d{4,})(?!\d)");
            if (plainMatch.Success)
            {
                return decimal.Parse(plainMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            return 0m;
        }

        private static DateTime ExtractDate(string normalized)
        {
            var today = DateTime.UtcNow.Date;

            if (normalized.Contains("hom qua", StringComparison.Ordinal))
            {
                return today.AddDays(-1);
            }

            if (normalized.Contains("tuan truoc", StringComparison.Ordinal))
            {
                return today.AddDays(-7);
            }

            if (normalized.Contains("hom nay", StringComparison.Ordinal) ||
                normalized.Contains("sang nay", StringComparison.Ordinal) ||
                normalized.Contains("chieu nay", StringComparison.Ordinal) ||
                normalized.Contains("toi nay", StringComparison.Ordinal))
            {
                return today;
            }

            var explicitDate = Regex.Match(normalized, @"(?<!\d)(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?(?!\d)");
            if (explicitDate.Success)
            {
                var day = int.Parse(explicitDate.Groups[1].Value, CultureInfo.InvariantCulture);
                var month = int.Parse(explicitDate.Groups[2].Value, CultureInfo.InvariantCulture);
                var year = explicitDate.Groups[3].Success
                    ? int.Parse(explicitDate.Groups[3].Value, CultureInfo.InvariantCulture)
                    : today.Year;

                if (year < 100)
                {
                    year += 2000;
                }

                try
                {
                    return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                }
                catch
                {
                    return today;
                }
            }

            return today;
        }

        private static string Normalize(string input)
        {
            var text = RemoveDiacritics(input ?? string.Empty).ToLowerInvariant();
            text = Regex.Replace(text, @"[^a-z0-9/\-\s\.,]", " ");
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

        private sealed record HistorySuggestion(
            Category Category,
            Wallet? Wallet,
            List<string> Reasons,
            List<string> MatchedTokens);
    }
}
