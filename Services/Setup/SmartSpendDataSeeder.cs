using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;
using SmartSpendAI.Security;

namespace SmartSpendAI.Services.Setup
{
    public class SmartSpendDataSeeder : ISmartSpendDataSeeder
    {
        private static readonly Role[] RequiredRoles =
        [
            new Role { RoleName = AppRoles.StandardUser },
            new Role { RoleName = AppRoles.SystemAdmin }
        ];

        private static readonly Category[] RequiredCategories =
        [
            new Category { Name = "An uong", Type = "Expense", Icon = "utensils", Color = "#ff7a18", IsSystem = true },
            new Category { Name = "Di chuyen", Type = "Expense", Icon = "car", Color = "#00b894", IsSystem = true },
            new Category { Name = "Giai tri", Type = "Expense", Icon = "film", Color = "#6c5ce7", IsSystem = true },
            new Category { Name = "Hoa don", Type = "Expense", Icon = "receipt", Color = "#ff4d4f", IsSystem = true },
            new Category { Name = "Mua sam", Type = "Expense", Icon = "bag", Color = "#0097e6", IsSystem = true },
            new Category { Name = "Luong", Type = "Income", Icon = "wallet", Color = "#2ecc71", IsSystem = true },
            new Category { Name = "Thuong", Type = "Income", Icon = "sparkles", Color = "#00cec9", IsSystem = true },
            new Category { Name = "Khac", Type = "Expense", Icon = "circle", Color = "#95a5a6", IsSystem = true }
        ];

        private readonly AppDbContext _dbContext;
        private readonly SmartSpendSeedOptions _options;
        private readonly ILogger<SmartSpendDataSeeder> _logger;

        public SmartSpendDataSeeder(
            AppDbContext dbContext,
            IOptions<SmartSpendSeedOptions> options,
            ILogger<SmartSpendDataSeeder> logger)
        {
            _dbContext = dbContext;
            _options = options.Value;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken)
        {
            if (!_options.Enabled)
            {
                return;
            }

            await EnsureBaselineDataAsync(cancellationToken);

            var admin = await UpsertUserAsync(
                _options.AdminUsername,
                _options.AdminFullName,
                _options.AdminEmail,
                _options.AdminPassword,
                AppRoles.SystemAdmin,
                cancellationToken);

            _logger.LogInformation("Đã đảm bảo tài khoản quản trị seed cho {Email}.", admin.Email);

            if (!_options.SeedDemoData)
            {
                return;
            }

            var demoUser = await UpsertUserAsync(
                _options.DemoUsername,
                _options.DemoFullName,
                _options.DemoEmail,
                _options.DemoPassword,
                AppRoles.StandardUser,
                cancellationToken);

            await SeedDemoFinanceAsync(demoUser, cancellationToken);
            _logger.LogInformation("Đã đảm bảo tài khoản demo seed cho {Email}.", demoUser.Email);
        }

        private async Task<User> UpsertUserAsync(
            string username,
            string fullName,
            string email,
            string password,
            string roleName,
            CancellationToken cancellationToken)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var normalizedUsername = username.Trim();
            var roleId = await _dbContext.Roles
                .Where(x => x.RoleName == roleName)
                .Select(x => x.RoleId)
                .FirstAsync(cancellationToken);

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(
                    x => x.Email == normalizedEmail || x.Username == normalizedUsername,
                    cancellationToken);

            if (user is null)
            {
                user = new User
                {
                    Username = normalizedUsername,
                    FullName = fullName.Trim(),
                    Email = normalizedEmail,
                    PasswordHash = PasswordHashUtility.HashPassword(password),
                    RoleId = roleId,
                    IsLocked = false,
                    IsEmailVerified = true,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Users.Add(user);
            }
            else
            {
                user.Username = normalizedUsername;
                user.FullName = fullName.Trim();
                user.Email = normalizedEmail;
                user.RoleId = roleId;
                user.IsLocked = false;
                user.IsEmailVerified = true;

                if (_options.ResetPasswordsOnSeed)
                {
                    user.PasswordHash = PasswordHashUtility.HashPassword(password);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }

        private async Task EnsureBaselineDataAsync(CancellationToken cancellationToken)
        {
            foreach (var requiredRole in RequiredRoles)
            {
                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(x => x.RoleName == requiredRole.RoleName, cancellationToken);

                if (role is null)
                {
                    _dbContext.Roles.Add(new Role
                    {
                        RoleName = requiredRole.RoleName
                    });
                }
            }

            foreach (var requiredCategory in RequiredCategories)
            {
                var category = await _dbContext.Categories
                    .FirstOrDefaultAsync(
                        x => x.Name == requiredCategory.Name && x.Type == requiredCategory.Type,
                        cancellationToken);

                if (category is null)
                {
                    _dbContext.Categories.Add(new Category
                    {
                        Name = requiredCategory.Name,
                        Type = requiredCategory.Type,
                        Icon = requiredCategory.Icon,
                        Color = requiredCategory.Color,
                        IsSystem = requiredCategory.IsSystem
                    });
                }
                else
                {
                    category.Icon = requiredCategory.Icon;
                    category.Color = requiredCategory.Color;
                    category.IsSystem = true;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task SeedDemoFinanceAsync(User demoUser, CancellationToken cancellationToken)
        {
            var walletCount = await _dbContext.Wallets.CountAsync(x => x.UserId == demoUser.UserId, cancellationToken);
            var transactionCount = await _dbContext.Transactions.CountAsync(x => x.UserId == demoUser.UserId, cancellationToken);
            var budgetCount = await _dbContext.Budgets.CountAsync(x => x.UserId == demoUser.UserId, cancellationToken);
            var alertCount = await _dbContext.BudgetAlerts.CountAsync(x => x.UserId == demoUser.UserId, cancellationToken);

            var hasCompleteDemoData =
                walletCount >= 3 &&
                transactionCount >= 20 &&
                budgetCount >= 4 &&
                alertCount >= 3;

            if (hasCompleteDemoData)
            {
                return;
            }

            if (walletCount > 0 || transactionCount > 0 || budgetCount > 0 || alertCount > 0)
            {
                _logger.LogWarning(
                    "Dữ liệu demo SmartSpend của người dùng {UserId} chưa đầy đủ. Đang đặt lại phần seed dở dang. Ví={WalletCount}, GiaoDịch={TransactionCount}, NgânSách={BudgetCount}, CảnhBáo={AlertCount}",
                    demoUser.UserId,
                    walletCount,
                    transactionCount,
                    budgetCount,
                    alertCount);

                await PurgeDemoFinanceAsync(demoUser.UserId, cancellationToken);
            }

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var previousMonthStart = monthStart.AddMonths(-1);
            var categoryMap = await _dbContext.Categories
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Name, x => x.CategoryId, cancellationToken);

            var cashWallet = new Wallet
            {
                UserId = demoUser.UserId,
                Name = "Vi tien mat",
                Type = "Cash",
                Balance = 1_200_000m,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            };

            var bankWallet = new Wallet
            {
                UserId = demoUser.UserId,
                Name = "Tai khoan ngan hang",
                Type = "Bank",
                Balance = 4_500_000m,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow
            };

            var savingsWallet = new Wallet
            {
                UserId = demoUser.UserId,
                Name = "Tiet kiem hoc ky",
                Type = "Savings",
                Balance = 10_000_000m,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Wallets.AddRange(cashWallet, bankWallet, savingsWallet);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var transfer = new TransferRecord
            {
                FromWalletId = bankWallet.WalletId,
                ToWalletId = cashWallet.WalletId,
                Amount = 1_000_000m,
                Note = "Rut tien mat cho sinh hoat va di chuyen",
                TransferDate = monthStart.AddDays(6),
                CreatedAt = DateTime.UtcNow
            };

            bankWallet.Balance -= transfer.Amount;
            cashWallet.Balance += transfer.Amount;
            _dbContext.Transfers.Add(transfer);

            var transactions = new List<TransactionEntry>
            {
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Luong"], "Income", 14_500_000m, "Luong thang truoc", previousMonthStart.AddDays(2), 98m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Mua sam"], "Expense", 350_000m, "Mua sach va do dung hoc tap", previousMonthStart.AddDays(6), 82m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["An uong"], "Expense", 40_000m, "Tra sua thang truoc", previousMonthStart.AddDays(9), 88m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["Giai tri"], "Expense", 150_000m, "Xem phim cuoi thang truoc", previousMonthStart.AddDays(14), 91m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["Di chuyen"], "Expense", 140_000m, "Do xang xe tuan cuoi thang truoc", previousMonthStart.AddDays(19), 84m),

                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Luong"], "Income", 15_000_000m, "Luong thang nay", monthStart.AddDays(1), 99m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Thuong"], "Income", 2_000_000m, "Thuong project nhom", monthStart.AddDays(3), 96m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Hoa don"], "Expense", 3_000_000m, "Tien nha va phi sinh hoat co dinh", monthStart.AddDays(4), 92m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Hoa don"], "Expense", 650_000m, "Dong tien dien nuoc wifi", monthStart.AddDays(7), 90m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["An uong"], "Expense", 620_000m, "Di cho mua do an cuoi tuan", monthStart.AddDays(8), 88m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["An uong"], "Expense", 480_000m, "An toi voi nhom do an", monthStart.AddDays(10), 86m),
                CreateTransaction(demoUser.UserId, bankWallet, categoryMap["Mua sam"], "Expense", 890_000m, "Mua ban phim va chuot", monthStart.AddDays(12), 80m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["An uong"], "Expense", 58_000m, "Tra sua 50k hom qua", monthStart.AddDays(14), 93m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["An uong"], "Expense", 75_000m, "Com trua sau gio hoc", monthStart.AddDays(15), 89m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["An uong"], "Expense", 110_000m, "Cafe toi voi ban", monthStart.AddDays(16), 87m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["An uong"], "Expense", 95_000m, "An vat luc lam bai", monthStart.AddDays(17), 84m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["Giai tri"], "Expense", 220_000m, "Di xem phim cuoi tuan", monthStart.AddDays(18), 95m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["Giai tri"], "Expense", 200_000m, "Karaoke voi lop", monthStart.AddDays(20), 91m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["Di chuyen"], "Expense", 180_000m, "Do xang xe", monthStart.AddDays(21), 90m),
                CreateTransaction(demoUser.UserId, cashWallet, categoryMap["Di chuyen"], "Expense", 120_000m, "Grab den truong", monthStart.AddDays(22), 88m)
            };

            foreach (var transaction in transactions)
            {
                ApplyWalletImpact(transaction.Wallet, transaction.Type, transaction.Amount);
            }

            _dbContext.Transactions.AddRange(transactions);

            var budgets = new[]
            {
                new Budget { UserId = demoUser.UserId, CategoryId = categoryMap["An uong"], Month = monthStart, LimitAmount = 1_500_000m },
                new Budget { UserId = demoUser.UserId, CategoryId = categoryMap["Hoa don"], Month = monthStart, LimitAmount = 3_800_000m },
                new Budget { UserId = demoUser.UserId, CategoryId = categoryMap["Giai tri"], Month = monthStart, LimitAmount = 350_000m },
                new Budget { UserId = demoUser.UserId, CategoryId = categoryMap["Di chuyen"], Month = monthStart, LimitAmount = 600_000m }
            };

            _dbContext.Budgets.AddRange(budgets);

            _dbContext.AuditLogs.AddRange(
                new AuditLog
                {
                    ActorUserId = demoUser.UserId,
                    Action = "SeededDemoWallets",
                    TargetType = "Wallet",
                    Metadata = "Đã tạo các ví mẫu cho bản demo SmartSpend.",
                    CreatedAt = DateTime.UtcNow
                },
                new AuditLog
                {
                    ActorUserId = demoUser.UserId,
                    Action = "SeededDemoTransactions",
                    TargetType = "Transaction",
                    Metadata = "Đã chèn lịch sử thu chi mẫu cho dashboard.",
                    CreatedAt = DateTime.UtcNow
                });

            await _dbContext.SaveChangesAsync(cancellationToken);

            var warningTransaction = transactions.First(x => x.Note == "Di cho mua do an cuoi tuan");
            var budgetWarningTransaction = transactions.First(x => x.Note == "Karaoke voi lop");
            var billWarningTransaction = transactions.First(x => x.Note == "Dong tien dien nuoc wifi");

            _dbContext.BudgetAlerts.AddRange(
                new BudgetAlert
                {
                    UserId = demoUser.UserId,
                    TransactionId = warningTransaction.TransactionEntryId,
                    Message = "Ban da dung khoang 96% ngan sach An uong trong thang nay.",
                    Level = "Warning",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-20)
                },
                new BudgetAlert
                {
                    UserId = demoUser.UserId,
                    TransactionId = billWarningTransaction.TransactionEntryId,
                    Message = "Ngan sach Hoa don da vuot 90%, can theo doi them.",
                    Level = "Warning",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-12)
                },
                new BudgetAlert
                {
                    UserId = demoUser.UserId,
                    TransactionId = budgetWarningTransaction.TransactionEntryId,
                    Message = "Ban da vuot ngan sach Giai tri thang nay.",
                    Level = "Danger",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5)
                });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task PurgeDemoFinanceAsync(int userId, CancellationToken cancellationToken)
        {
            var walletIds = await _dbContext.Wallets
                .Where(x => x.UserId == userId)
                .Select(x => x.WalletId)
                .ToListAsync(cancellationToken);

            var alerts = await _dbContext.BudgetAlerts
                .Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);
            _dbContext.BudgetAlerts.RemoveRange(alerts);

            var transactions = await _dbContext.Transactions
                .Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);
            _dbContext.Transactions.RemoveRange(transactions);

            var budgets = await _dbContext.Budgets
                .Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);
            _dbContext.Budgets.RemoveRange(budgets);

            if (walletIds.Count > 0)
            {
                var transfers = await _dbContext.Transfers
                    .Where(x => walletIds.Contains(x.FromWalletId) || walletIds.Contains(x.ToWalletId))
                    .ToListAsync(cancellationToken);
                _dbContext.Transfers.RemoveRange(transfers);
            }

            var auditLogs = await _dbContext.AuditLogs
                .Where(x => x.ActorUserId == userId && x.Action.StartsWith("SeededDemo"))
                .ToListAsync(cancellationToken);
            _dbContext.AuditLogs.RemoveRange(auditLogs);

            var wallets = await _dbContext.Wallets
                .Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);
            _dbContext.Wallets.RemoveRange(wallets);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static TransactionEntry CreateTransaction(
            int userId,
            Wallet wallet,
            int categoryId,
            string type,
            decimal amount,
            string note,
            DateTime transactionDate,
            decimal confidence)
        {
            return new TransactionEntry
            {
                UserId = userId,
                WalletId = wallet.WalletId,
                Wallet = wallet,
                CategoryId = categoryId,
                Type = type,
                Amount = amount,
                Note = note,
                TransactionDate = transactionDate,
                AiConfidence = confidence,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static void ApplyWalletImpact(Wallet wallet, string type, decimal amount)
        {
            var factor = string.Equals(type, "Income", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            wallet.Balance += factor * amount;
        }
    }
}
