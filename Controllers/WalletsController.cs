using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Finance;
using SmartSpendAI.Security;

namespace SmartSpendAI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class WalletsController : ApiControllerBase
    {
        private readonly AppDbContext _dbContext;

        public WalletsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<WalletResponse>>> GetWallets(CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var wallets = await _dbContext.Wallets
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .Select(x => new WalletResponse
                {
                    WalletId = x.WalletId,
                    Name = x.Name,
                    Type = x.Type,
                    Balance = x.Balance,
                    IsDefault = x.IsDefault,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(wallets);
        }

        [HttpPost]
        public async Task<ActionResult<WalletResponse>> CreateWallet([FromBody] WalletRequest request, CancellationToken cancellationToken)
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

            if (!IsSystemAdmin())
            {
                var walletCount = await _dbContext.Wallets.CountAsync(x => x.UserId == userId, cancellationToken);
                if (walletCount >= 3)
                {
                    return BadRequest(new { message = "Tài khoản Standard chỉ tạo tối đa 3 ví." });
                }
            }

            if (request.IsDefault)
            {
                await SetDefaultWalletAsync(userId.Value, null, cancellationToken);
            }

            var wallet = new Wallet
            {
                UserId = userId.Value,
                Name = request.Name.Trim(),
                Type = request.Type.Trim(),
                Balance = request.InitialBalance,
                IsDefault = request.IsDefault,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Wallets.Add(wallet);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "WalletCreated",
                TargetType = "Wallet",
                Metadata = wallet.Name,
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(nameof(GetWallets), new
            {
                walletId = wallet.WalletId
            }, new WalletResponse
            {
                WalletId = wallet.WalletId,
                Name = wallet.Name,
                Type = wallet.Type,
                Balance = wallet.Balance,
                IsDefault = wallet.IsDefault,
                CreatedAt = wallet.CreatedAt
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateWallet(int id, [FromBody] WalletRequest request, CancellationToken cancellationToken)
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

            var wallet = await _dbContext.Wallets.FirstOrDefaultAsync(x => x.WalletId == id && x.UserId == userId, cancellationToken);
            if (wallet is null)
            {
                return NotFound(new { message = "Không tìm thấy ví." });
            }

            wallet.Name = request.Name.Trim();
            wallet.Type = request.Type.Trim();
            wallet.IsDefault = request.IsDefault;

            if (request.IsDefault)
            {
                await SetDefaultWalletAsync(userId.Value, wallet.WalletId, cancellationToken);
            }

            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "WalletUpdated",
                TargetType = "Wallet",
                TargetId = wallet.WalletId.ToString(),
                Metadata = wallet.Name,
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Cap nhat vi thanh cong." });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteWallet(int id, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            var wallet = await _dbContext.Wallets
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(x => x.WalletId == id && x.UserId == userId, cancellationToken);

            if (wallet is null)
            {
                return NotFound(new { message = "Không tìm thấy ví." });
            }

            if (wallet.Transactions.Count > 0)
            {
                return BadRequest(new { message = "Không thể xóa ví đã có giao dịch." });
            }

            _dbContext.Wallets.Remove(wallet);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "WalletDeleted",
                TargetType = "Wallet",
                TargetId = wallet.WalletId.ToString(),
                Metadata = wallet.Name,
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Da xoa vi." });
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] WalletTransferRequest request, CancellationToken cancellationToken)
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

            if (request.FromWalletId == request.ToWalletId)
            {
                return BadRequest(new { message = "Vi nguon va vi dich phai khac nhau." });
            }

            var wallets = await _dbContext.Wallets
                .Where(x => x.UserId == userId && (x.WalletId == request.FromWalletId || x.WalletId == request.ToWalletId))
                .ToListAsync(cancellationToken);

            var fromWallet = wallets.FirstOrDefault(x => x.WalletId == request.FromWalletId);
            var toWallet = wallets.FirstOrDefault(x => x.WalletId == request.ToWalletId);

            if (fromWallet is null || toWallet is null)
            {
                return BadRequest(new { message = "Ví nguồn hoặc ví đích không hợp lệ." });
            }

            if (fromWallet.Balance < request.Amount)
            {
                return BadRequest(new { message = "Số dư ví nguồn không đủ." });
            }

            fromWallet.Balance -= request.Amount;
            toWallet.Balance += request.Amount;

            _dbContext.Transfers.Add(new TransferRecord
            {
                FromWalletId = fromWallet.WalletId,
                ToWalletId = toWallet.WalletId,
                Amount = request.Amount,
                Note = request.Note.Trim(),
                TransferDate = request.TransferDate?.Date ?? DateTime.UtcNow.Date,
                CreatedAt = DateTime.UtcNow
            });

            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId,
                Action = "WalletTransferred",
                TargetType = "Transfer",
                Metadata = $"{fromWallet.Name}->{toWallet.Name}:{request.Amount}",
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Chuyen tien thanh cong." });
        }

        private async Task SetDefaultWalletAsync(int userId, int? skipWalletId, CancellationToken cancellationToken)
        {
            var existingDefaults = await _dbContext.Wallets
                .Where(x => x.UserId == userId && x.WalletId != skipWalletId && x.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var existingDefault in existingDefaults)
            {
                existingDefault.IsDefault = false;
            }
        }
    }
}
