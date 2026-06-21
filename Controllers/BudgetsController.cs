using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Finance;

namespace SmartSpendAI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class BudgetsController : ApiControllerBase
    {
        private readonly AppDbContext _dbContext;

        public BudgetsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BudgetResponse>>> GetBudgets([FromQuery] DateTime? month, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var targetMonth = NormalizeMonth(month ?? DateTime.UtcNow);
            var budgets = await _dbContext.Budgets
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.UserId == userId && x.Month == targetMonth)
                .OrderBy(x => x.Category.Name)
                .ToListAsync(cancellationToken);

            var responses = new List<BudgetResponse>();
            foreach (var budget in budgets)
            {
                responses.Add(await MapBudgetAsync(userId.Value, budget, cancellationToken));
            }

            return Ok(responses);
        }

        [HttpPost]
        public async Task<ActionResult<BudgetResponse>> UpsertBudget([FromBody] BudgetRequest request, CancellationToken cancellationToken)
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

            var targetMonth = NormalizeMonth(request.Month);
            var budget = await _dbContext.Budgets
                .Include(x => x.Category)
                .FirstOrDefaultAsync(x => x.UserId == userId && x.CategoryId == request.CategoryId && x.Month == targetMonth, cancellationToken);

            if (budget is null)
            {
                var category = await _dbContext.Categories.FirstOrDefaultAsync(x => x.CategoryId == request.CategoryId, cancellationToken);
                if (category is null)
                {
                    return BadRequest(new { message = "Danh mục không hợp lệ." });
                }

                budget = new Budget
                {
                    UserId = userId.Value,
                    CategoryId = request.CategoryId,
                    Month = targetMonth,
                    LimitAmount = request.LimitAmount
                };
                _dbContext.Budgets.Add(budget);
            }
            else
            {
                budget.LimitAmount = request.LimitAmount;
            }

            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "BudgetUpserted",
                TargetType = "Budget",
                Metadata = $"{request.CategoryId}:{targetMonth:yyyy-MM}",
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            budget = await _dbContext.Budgets.Include(x => x.Category)
                .FirstAsync(x => x.BudgetId == budget.BudgetId, cancellationToken);

            return Ok(await MapBudgetAsync(userId.Value, budget, cancellationToken));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteBudget(int id, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var budget = await _dbContext.Budgets.FirstOrDefaultAsync(x => x.BudgetId == id && x.UserId == userId, cancellationToken);
            if (budget is null)
            {
                return NotFound(new { message = "Không tìm thấy ngân sách." });
            }

            _dbContext.Budgets.Remove(budget);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Đã xóa ngân sách." });
        }

        private async Task<BudgetResponse> MapBudgetAsync(int userId, Budget budget, CancellationToken cancellationToken)
        {
            var spent = await _dbContext.Transactions
                .Where(x => x.UserId == userId &&
                            x.CategoryId == budget.CategoryId &&
                            x.Type == "Expense" &&
                            x.TransactionDate >= budget.Month &&
                            x.TransactionDate < budget.Month.AddMonths(1))
                .SumAsync(x => x.Amount, cancellationToken);

            var progress = budget.LimitAmount <= 0 ? 0 : decimal.Round(spent / budget.LimitAmount * 100, 2);
            var status = progress >= 100 ? "Danger" : progress >= 80 ? "Warning" : "Safe";

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
                Status = status
            };
        }

        private static DateTime NormalizeMonth(DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}
