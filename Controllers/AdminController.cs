using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Admin;
using SmartSpendAI.Security;

namespace SmartSpendAI.Controllers
{
    [Authorize(Policy = AppPolicies.AdminOnly)]
    [Route("api/admin")]
    public class AdminController : ApiControllerBase
    {
        private readonly AppDbContext _dbContext;

        public AdminController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("summary")]
        public async Task<ActionResult<AdminSystemSummaryResponse>> GetSummary(CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            return Ok(new AdminSystemSummaryResponse
            {
                NewUsersToday = await _dbContext.Users.CountAsync(x => x.CreatedAt >= today, cancellationToken),
                TransactionsToday = await _dbContext.Transactions.CountAsync(x => x.TransactionDate >= today, cancellationToken),
                TotalUsers = await _dbContext.Users.CountAsync(cancellationToken),
                TotalKeywords = await _dbContext.Keywords.CountAsync(cancellationToken)
            });
        }

        [HttpGet("users")]
        public async Task<ActionResult<IEnumerable<AdminUserSummaryResponse>>> GetUsers(CancellationToken cancellationToken)
        {
            var users = await _dbContext.Users
                .AsNoTracking()
                .Include(x => x.Role)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new AdminUserSummaryResponse
                {
                    UserId = x.UserId,
                    Username = x.Username,
                    FullName = x.FullName,
                    Email = x.Email,
                    Role = x.Role.RoleName,
                    IsLocked = x.IsLocked,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(users);
        }

        [HttpPost("users/{id:int}/lock")]
        public async Task<IActionResult> LockUser(int id, CancellationToken cancellationToken)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
            if (user is null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            user.IsLocked = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Đã khóa tài khoản." });
        }

        [HttpPost("users/{id:int}/unlock")]
        public async Task<IActionResult> UnlockUser(int id, CancellationToken cancellationToken)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
            if (user is null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            user.IsLocked = false;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Đã mở khóa tài khoản." });
        }

        [HttpGet("keywords")]
        public async Task<ActionResult<IEnumerable<KeywordResponse>>> GetKeywords(CancellationToken cancellationToken)
        {
            var keywords = await _dbContext.Keywords
                .AsNoTracking()
                .Include(x => x.Category)
                .OrderBy(x => x.Word)
                .Select(x => new KeywordResponse
                {
                    KeywordId = x.KeywordEntryId,
                    Word = x.Word,
                    CategoryId = x.CategoryId,
                    CategoryName = x.Category.Name,
                    Weight = x.Weight,
                    IsActive = x.IsActive
                })
                .ToListAsync(cancellationToken);

            return Ok(keywords);
        }

        [HttpPost("keywords")]
        public async Task<IActionResult> CreateKeyword([FromBody] KeywordRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var keyword = new KeywordEntry
            {
                Word = request.Word.Trim().ToLowerInvariant(),
                CategoryId = request.CategoryId,
                Weight = request.Weight,
                IsActive = request.IsActive
            };

            _dbContext.Keywords.Add(keyword);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Đã thêm từ khóa." });
        }

        [HttpPut("keywords/{id:int}")]
        public async Task<IActionResult> UpdateKeyword(int id, [FromBody] KeywordRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var keyword = await _dbContext.Keywords.FirstOrDefaultAsync(x => x.KeywordEntryId == id, cancellationToken);
            if (keyword is null)
            {
                return NotFound(new { message = "Không tìm thấy từ khóa." });
            }

            keyword.Word = request.Word.Trim().ToLowerInvariant();
            keyword.CategoryId = request.CategoryId;
            keyword.Weight = request.Weight;
            keyword.IsActive = request.IsActive;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Đã cập nhật từ khóa." });
        }

        [HttpDelete("keywords/{id:int}")]
        public async Task<IActionResult> DeleteKeyword(int id, CancellationToken cancellationToken)
        {
            var keyword = await _dbContext.Keywords.FirstOrDefaultAsync(x => x.KeywordEntryId == id, cancellationToken);
            if (keyword is null)
            {
                return NotFound(new { message = "Không tìm thấy từ khóa." });
            }

            _dbContext.Keywords.Remove(keyword);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Đã xóa từ khóa." });
        }

        [HttpGet("audit-logs")]
        public async Task<ActionResult<IEnumerable<AdminAuditLogResponse>>> GetAuditLogs(
            [FromQuery] int take = 50,
            CancellationToken cancellationToken = default)
        {
            var logs = await _dbContext.AuditLogs
                .AsNoTracking()
                .Include(x => x.ActorUser)
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Clamp(take, 1, 200))
                .Select(x => new AdminAuditLogResponse
                {
                    AuditLogId = x.AuditLogId,
                    Actor = x.ActorUser != null ? x.ActorUser.Username : "hệ thống",
                    Action = x.Action,
                    TargetType = x.TargetType,
                    TargetId = x.TargetId,
                    Metadata = x.Metadata,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(logs);
        }
    }
}
