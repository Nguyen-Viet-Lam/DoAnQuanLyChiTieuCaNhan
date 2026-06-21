using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Admin;
using SmartSpendAI.Security;

namespace SmartSpendAI.Controllers
{
    [Authorize(Policy = AppPolicies.AdminOnly)]
    [Route("api/admin/categories")]
    public class AdminCategoriesController : ApiControllerBase
    {
        private readonly AppDbContext _dbContext;

        public AdminCategoriesController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AdminCategoryResponse>>> GetCategories(
            [FromQuery] string? type,
            CancellationToken cancellationToken)
        {
            var query = _dbContext.Categories.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(type))
            {
                var normalizedType = NormalizeCategoryType(type);
                if (normalizedType is null)
                {
                    return BadRequest(new { message = "Loai danh muc chi ho tro Expense hoac Income." });
                }

                query = query.Where(x => x.Type == normalizedType);
            }

            var categories = await query
                .OrderBy(x => x.Type)
                .ThenBy(x => x.Name)
                .Select(x => new AdminCategoryResponse
                {
                    CategoryId = x.CategoryId,
                    Name = x.Name,
                    Type = x.Type,
                    Icon = x.Icon,
                    Color = x.Color,
                    IsSystem = x.IsSystem
                })
                .ToListAsync(cancellationToken);

            return Ok(categories);
        }

        [HttpPost]
        public async Task<ActionResult<AdminCategoryResponse>> CreateCategory(
            [FromBody] AdminCategoryRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var normalizedName = NormalizeName(request.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return BadRequest(new { message = "Tên danh mục không hợp lệ." });
            }

            var normalizedType = NormalizeCategoryType(request.Type);
            if (normalizedType is null)
            {
                return BadRequest(new { message = "Loai danh muc chi ho tro Expense hoac Income." });
            }

            var duplicated = await _dbContext.Categories
                .AnyAsync(x => x.Name == normalizedName && x.Type == normalizedType, cancellationToken);
            if (duplicated)
            {
                return Conflict(new { message = "Danh mục này đã tồn tại." });
            }

            var category = new Category
            {
                Name = normalizedName,
                Type = normalizedType,
                Icon = NormalizeIcon(request.Icon),
                Color = NormalizeColor(request.Color),
                IsSystem = request.IsSystem
            };

            _dbContext.Categories.Add(category);
            AddAudit("AdminCategoryCreated", "Category", string.Empty, $"{category.Name}|{category.Type}");
            await _dbContext.SaveChangesAsync(cancellationToken);

            var response = MapCategory(category);
            return Created($"/api/admin/categories/{category.CategoryId}", response);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<AdminCategoryResponse>> UpdateCategory(
            int id,
            [FromBody] AdminCategoryRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var category = await _dbContext.Categories.FirstOrDefaultAsync(x => x.CategoryId == id, cancellationToken);
            if (category is null)
            {
                return NotFound(new { message = "Không tìm thấy danh mục." });
            }

            var normalizedName = NormalizeName(request.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return BadRequest(new { message = "Tên danh mục không hợp lệ." });
            }

            var normalizedType = NormalizeCategoryType(request.Type);
            if (normalizedType is null)
            {
                return BadRequest(new { message = "Loai danh muc chi ho tro Expense hoac Income." });
            }

            var duplicated = await _dbContext.Categories
                .AnyAsync(x => x.CategoryId != id && x.Name == normalizedName && x.Type == normalizedType, cancellationToken);
            if (duplicated)
            {
                return Conflict(new { message = "Danh mục này đã tồn tại." });
            }

            category.Name = normalizedName;
            category.Type = normalizedType;
            category.Icon = NormalizeIcon(request.Icon);
            category.Color = NormalizeColor(request.Color);
            category.IsSystem = request.IsSystem;

            AddAudit("AdminCategoryUpdated", "Category", category.CategoryId.ToString(), $"{category.Name}|{category.Type}");
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(MapCategory(category));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
        {
            var category = await _dbContext.Categories.FirstOrDefaultAsync(x => x.CategoryId == id, cancellationToken);
            if (category is null)
            {
                return NotFound(new { message = "Không tìm thấy danh mục." });
            }

            var isInUse = await _dbContext.Transactions.AnyAsync(x => x.CategoryId == id, cancellationToken) ||
                          await _dbContext.Budgets.AnyAsync(x => x.CategoryId == id, cancellationToken) ||
                          await _dbContext.Keywords.AnyAsync(x => x.CategoryId == id, cancellationToken) ||
                          await _dbContext.UserPersonalKeywords.AnyAsync(x => x.CategoryId == id, cancellationToken);
            if (isInUse)
            {
                return BadRequest(new { message = "Không thể xóa danh mục đang được sử dụng." });
            }

            _dbContext.Categories.Remove(category);
            AddAudit("AdminCategoryDeleted", "Category", category.CategoryId.ToString(), category.Name);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Da xoa danh muc." });
        }

        private void AddAudit(string action, string targetType, string targetId, string metadata)
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = GetCurrentUserId(),
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Metadata = metadata,
                CreatedAt = DateTime.UtcNow
            });
        }

        private static AdminCategoryResponse MapCategory(Category category)
        {
            return new AdminCategoryResponse
            {
                CategoryId = category.CategoryId,
                Name = category.Name,
                Type = category.Type,
                Icon = category.Icon,
                Color = category.Color,
                IsSystem = category.IsSystem
            };
        }

        private static string NormalizeName(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string NormalizeIcon(string? value)
        {
            var icon = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(icon) ? "circle" : icon;
        }

        private static string NormalizeColor(string? value)
        {
            var color = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(color) ? "#48d1a0" : color;
        }

        private static string? NormalizeCategoryType(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "expense" => "Expense",
                "income" => "Income",
                _ => null
            };
        }
    }
}
