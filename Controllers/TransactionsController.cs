using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Dashboard;
using SmartSpendAI.Models.Dtos.Finance;
using SmartSpendAI.Services.AI;
using SmartSpendAI.Services.Finance;
using SmartSpendAI.Services.Realtime;

namespace SmartSpendAI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class TransactionsController : ApiControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IHubContext<BudgetAlertsHub> _hubContext;
        private readonly ITransactionExportService _transactionExportService;
        private readonly ISmartReminderService _smartReminderService;

        public TransactionsController(
            AppDbContext dbContext,
            IHubContext<BudgetAlertsHub> hubContext,
            ITransactionExportService transactionExportService,
            ISmartReminderService smartReminderService)
        {
            _dbContext = dbContext;
            _hubContext = hubContext;
            _transactionExportService = transactionExportService;
            _smartReminderService = smartReminderService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TransactionResponse>>> GetTransactions(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? walletId,
            [FromQuery] int? categoryId,
            [FromQuery] string? type,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var query = _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.Wallet)
                .Include(x => x.Category)
                .Where(x => x.UserId == userId);

            if (from.HasValue)
            {
                query = query.Where(x => x.TransactionDate >= from.Value.Date);
            }

            if (to.HasValue)
            {
                query = query.Where(x => x.TransactionDate < to.Value.Date.AddDays(1));
            }

            if (walletId.HasValue)
            {
                query = query.Where(x => x.WalletId == walletId.Value);
            }

            if (categoryId.HasValue)
            {
                query = query.Where(x => x.CategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(x => x.Type == type);
            }

            var transactions = await query
                .OrderByDescending(x => x.TransactionDate)
                .ThenByDescending(x => x.TransactionEntryId)
                .Select(x => new TransactionResponse
                {
                    TransactionId = x.TransactionEntryId,
                    WalletId = x.WalletId,
                    WalletName = x.Wallet.Name,
                    CategoryId = x.CategoryId,
                    CategoryName = x.Category.Name,
                    Type = x.Type,
                    Amount = x.Amount,
                    Note = x.Note,
                    TransactionDate = x.TransactionDate,
                    AiConfidence = x.AiConfidence,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(transactions);
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportTransactions(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? walletId,
            [FromQuery] int? categoryId,
            [FromQuery] string? type,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var payload = await _transactionExportService.ExportAsync(
                userId.Value,
                new TransactionExportFilter
                {
                    From = from,
                    To = to,
                    WalletId = walletId,
                    CategoryId = categoryId,
                    Type = type
                },
                cancellationToken);

            var fileName = $"smartspend-transactions-{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(
                payload,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        [HttpPost]
        public async Task<ActionResult<TransactionResponse>> CreateTransaction([FromBody] TransactionRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var wallet = await _dbContext.Wallets.FirstOrDefaultAsync(x => x.WalletId == request.WalletId && x.UserId == userId, cancellationToken);
            var category = await _dbContext.Categories.FirstOrDefaultAsync(x => x.CategoryId == request.CategoryId, cancellationToken);

            if (wallet is null || category is null)
            {
                return BadRequest(new { message = "Ví hoặc danh mục không hợp lệ." });
            }

            var transaction = new TransactionEntry
            {
                UserId = userId.Value,
                WalletId = request.WalletId,
                CategoryId = request.CategoryId,
                Type = request.Type.Trim(),
                Amount = request.Amount,
                Note = request.Note.Trim(),
                TransactionDate = request.TransactionDate.ToUniversalTime(),
                ReceiptImagePath = request.ReceiptImagePath,
                AiConfidence = 0,
                CreatedAt = DateTime.UtcNow
            };

            ApplyWalletImpact(wallet, transaction.Type, transaction.Amount, reverse: false);
            _dbContext.Transactions.Add(transaction);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "TransactionCreated",
                TargetType = "Transaction",
                Metadata = transaction.Note,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CreateAndBroadcastBudgetAlertsAsync(userId.Value, transaction, cancellationToken);
            await BroadcastAiReminderAsync(userId.Value, cancellationToken);

            return Ok(new TransactionResponse
            {
                TransactionId = transaction.TransactionEntryId,
                WalletId = wallet.WalletId,
                WalletName = wallet.Name,
                CategoryId = category.CategoryId,
                CategoryName = category.Name,
                Type = transaction.Type,
                Amount = transaction.Amount,
                Note = transaction.Note,
                TransactionDate = transaction.TransactionDate,
                AiConfidence = transaction.AiConfidence,
                CreatedAt = transaction.CreatedAt
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateTransaction(int id, [FromBody] TransactionRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var transaction = await _dbContext.Transactions.FirstOrDefaultAsync(x => x.TransactionEntryId == id && x.UserId == userId, cancellationToken);
            if (transaction is null)
            {
                return NotFound(new { message = "Không tìm thấy giao dịch." });
            }

            var oldWallet = await _dbContext.Wallets.FirstAsync(x => x.WalletId == transaction.WalletId, cancellationToken);
            var newWallet = await _dbContext.Wallets.FirstOrDefaultAsync(x => x.WalletId == request.WalletId && x.UserId == userId, cancellationToken);
            var category = await _dbContext.Categories.FirstOrDefaultAsync(x => x.CategoryId == request.CategoryId, cancellationToken);
            if (newWallet is null || category is null)
            {
                return BadRequest(new { message = "Ví hoặc danh mục không hợp lệ." });
            }

            ApplyWalletImpact(oldWallet, transaction.Type, transaction.Amount, reverse: true);
            transaction.WalletId = request.WalletId;
            transaction.CategoryId = request.CategoryId;
            transaction.Type = request.Type.Trim();
            transaction.Amount = request.Amount;
            transaction.Note = request.Note.Trim();
            transaction.TransactionDate = request.TransactionDate.ToUniversalTime();
            transaction.ReceiptImagePath = request.ReceiptImagePath;
            ApplyWalletImpact(newWallet, transaction.Type, transaction.Amount, reverse: false);

            _dbContext.BudgetAlerts.RemoveRange(_dbContext.BudgetAlerts.Where(x => x.TransactionId == transaction.TransactionEntryId));
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "TransactionUpdated",
                TargetType = "Transaction",
                TargetId = transaction.TransactionEntryId.ToString(),
                Metadata = transaction.Note,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CreateAndBroadcastBudgetAlertsAsync(userId.Value, transaction, cancellationToken);
            await BroadcastAiReminderAsync(userId.Value, cancellationToken);

            return Ok(new { message = "Cap nhat giao dich thanh cong." });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteTransaction(int id, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var transaction = await _dbContext.Transactions.FirstOrDefaultAsync(x => x.TransactionEntryId == id && x.UserId == userId, cancellationToken);
            if (transaction is null)
            {
                return NotFound(new { message = "Không tìm thấy giao dịch." });
            }

            var wallet = await _dbContext.Wallets.FirstAsync(x => x.WalletId == transaction.WalletId, cancellationToken);
            ApplyWalletImpact(wallet, transaction.Type, transaction.Amount, reverse: true);
            _dbContext.BudgetAlerts.RemoveRange(_dbContext.BudgetAlerts.Where(x => x.TransactionId == transaction.TransactionEntryId));
            _dbContext.Transactions.Remove(transaction);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "TransactionDeleted",
                TargetType = "Transaction",
                TargetId = transaction.TransactionEntryId.ToString(),
                Metadata = transaction.Note,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await BroadcastAiReminderAsync(userId.Value, cancellationToken);
            return Ok(new { message = "Da xoa giao dich." });
        }

        private static void ApplyWalletImpact(Wallet wallet, string type, decimal amount, bool reverse)
        {
            var factor = string.Equals(type, "Income", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            if (reverse)
            {
                factor *= -1;
            }

            wallet.Balance += factor * amount;
        }

        private async Task CreateAndBroadcastBudgetAlertsAsync(int userId, TransactionEntry transaction, CancellationToken cancellationToken)
        {
            var monthStart = new DateTime(transaction.TransactionDate.Year, transaction.TransactionDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var budgets = await _dbContext.Budgets
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.Month == monthStart && x.CategoryId == transaction.CategoryId)
                .ToListAsync(cancellationToken);

            foreach (var budget in budgets)
            {
                var spentAmount = await _dbContext.Transactions
                    .Where(x => x.UserId == userId &&
                                x.CategoryId == budget.CategoryId &&
                                x.Type == "Expense" &&
                                x.TransactionDate >= monthStart &&
                                x.TransactionDate < monthStart.AddMonths(1))
                    .SumAsync(x => x.Amount, cancellationToken);

                var percentage = budget.LimitAmount <= 0 ? 0 : spentAmount / budget.LimitAmount * 100;
                if (percentage < 80)
                {
                    continue;
                }

                var level = percentage >= 100 ? "Danger" : "Warning";
                var message = percentage >= 100
                    ? $"Bạn đã vượt ngân sách {budget.Category.Name} tháng này."
                    : $"Bạn đã dùng {percentage:0}% ngân sách {budget.Category.Name}.";

                var alert = new BudgetAlert
                {
                    UserId = userId,
                    TransactionId = transaction.TransactionEntryId,
                    Message = message,
                    Level = level,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.BudgetAlerts.Add(alert);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _hubContext.Clients.Group($"user:{userId}")
                    .SendAsync("budgetAlert", new
                    {
                        alertId = alert.BudgetAlertId,
                        message = alert.Message,
                        level = alert.Level,
                        createdAt = alert.CreatedAt
                    }, cancellationToken);
            }
        }

        private async Task BroadcastAiReminderAsync(int userId, CancellationToken cancellationToken)
        {
            var reminders = await _smartReminderService.GetMonthlyRemindersAsync(userId, cancellationToken);
            var reminder = reminders.FirstOrDefault();
            if (reminder is null)
            {
                return;
            }

            await _hubContext.Clients.Group($"user:{userId}")
                .SendAsync("aiReminder", new
                {
                    reminderId = $"ai-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                    kind = "AiReminder",
                    source = "AI",
                    message = reminder.Message,
                    level = reminder.Level,
                    tone = reminder.Tone,
                    categoryName = reminder.CategoryName,
                    percentage = reminder.Percentage,
                    createdAt = reminder.CreatedAt
                }, cancellationToken);
        }
    }
}
