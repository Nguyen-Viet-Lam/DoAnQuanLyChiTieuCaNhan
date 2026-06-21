using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Auth;
using SmartSpendAI.Security;
using SmartSpendAI.Services.Auth;
using SmartSpendAI.Services.Email;
using SmartSpendAI.Services.Otp;

namespace SmartSpendAI.Tests.Auth;

public sealed class AuthOtpFlowTests
{
    [Fact]
    public async Task VerifyRegisterOtpAsync_MarksUserAsVerified_WhenOtpIsValid()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "otp-user@example.com", "StrongPass1", isEmailVerified: false);
        await SeedRegisterOtpAsync(dbContext, user, "123456", DateTime.UtcNow.AddMinutes(5));

        var otpService = CreateOtpService(dbContext);

        var result = await otpService.VerifyRegisterOtpAsync(user.Email, "123456", CancellationToken.None);

        Assert.True(result.Success);

        var savedUser = await dbContext.Users.AsNoTracking().SingleAsync(x => x.UserId == user.UserId);
        var savedOtp = await dbContext.EmailVerificationOtps.AsNoTracking().SingleAsync(x => x.UserId == user.UserId);
        Assert.True(savedUser.IsEmailVerified);
        Assert.True(savedOtp.IsUsed);
        Assert.NotNull(savedOtp.UsedAt);
    }

    [Fact]
    public async Task LoginAsync_BlocksUnverifiedUser_And_AllowsAfterOtpVerification()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "login-flow@example.com", "StrongPass1", isEmailVerified: false);
        await SeedRegisterOtpAsync(dbContext, user, "654321", DateTime.UtcNow.AddMinutes(5));

        var authService = CreateAuthService(dbContext);

        var blockedResult = await authService.LoginAsync(
            new LoginRequest
            {
                EmailOrUsername = user.Email,
                Password = "StrongPass1",
                RememberMe = false
            },
            CancellationToken.None);

        Assert.False(blockedResult.Success);
        Assert.Equal(403, blockedResult.StatusCode);

        var otpService = CreateOtpService(dbContext);
        var verifyResult = await otpService.VerifyRegisterOtpAsync(user.Email, "654321", CancellationToken.None);
        Assert.True(verifyResult.Success);

        var allowedResult = await authService.LoginAsync(
            new LoginRequest
            {
                EmailOrUsername = user.Email,
                Password = "StrongPass1",
                RememberMe = false
            },
            CancellationToken.None);

        Assert.True(allowedResult.Success);
        Assert.Equal(200, allowedResult.StatusCode);
        Assert.NotNull(allowedResult.Response);
        Assert.Equal(user.Email, allowedResult.Response!.Email);
    }

    [Fact]
    public async Task LoginAsync_PersistsLoginAudit_WhenNewDeviceAlertEmailFails()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "audit-user@example.com", "StrongPass1", isEmailVerified: true);
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = user.UserId,
            Action = "UserLoginSuccess",
            TargetType = "User",
            TargetId = user.UserId.ToString(),
            Metadata = "ip=198.51.100.9;ua=legacy-agent",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await dbContext.SaveChangesAsync();

        var authService = CreateAuthService(
            dbContext,
            emailSender: new ThrowingEmailSender(),
            httpContextAccessor: CreateHttpContextAccessor("203.0.113.77", "new-device-agent"));

        var result = await authService.LoginAsync(
            new LoginRequest
            {
                EmailOrUsername = user.Email,
                Password = "StrongPass1",
                RememberMe = false
            },
            CancellationToken.None);

        Assert.True(result.Success);

        var loginAudits = await dbContext.AuditLogs
            .Where(x => x.ActorUserId == user.UserId && x.Action == "UserLoginSuccess")
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, loginAudits.Count);
        Assert.Contains(loginAudits, x => x.Metadata == "ip=203.0.113.77;ua=new-device-agent");
        Assert.DoesNotContain(
            await dbContext.AuditLogs.Where(x => x.ActorUserId == user.UserId).ToListAsync(),
            x => x.Action == "UserLoginNewDeviceAlertSent");
    }

    [Fact]
    public async Task ResetPasswordAsync_ReturnsFailure_WhenOtpIsExpired()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "expired-reset@example.com", "StrongPass1", isEmailVerified: true);
        await SeedOtpAsync(dbContext, user, "111111", OtpPurposes.ResetPassword, DateTime.UtcNow.AddMinutes(-1));

        var authService = CreateAuthService(dbContext, otpService: CreateOtpService(dbContext));

        var result = await authService.ResetPasswordAsync(
            new ResetPasswordRequest
            {
                Email = user.Email,
                OtpCode = "111111",
                NewPassword = "NewStrongPass1",
                ConfirmPassword = "NewStrongPass1"
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OTP da het han.", result.Message);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReturnsFailure_WhenOtpIsIncorrect()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "wrong-reset@example.com", "StrongPass1", isEmailVerified: true);
        await SeedOtpAsync(dbContext, user, "222222", OtpPurposes.ResetPassword, DateTime.UtcNow.AddMinutes(5));

        var authService = CreateAuthService(dbContext, otpService: CreateOtpService(dbContext));

        var result = await authService.ResetPasswordAsync(
            new ResetPasswordRequest
            {
                Email = user.Email,
                OtpCode = "999999",
                NewPassword = "NewStrongPass1",
                ConfirmPassword = "NewStrongPass1"
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OTP không chính xác.", result.Message);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReturnsFailure_WhenOtpExceedsAttemptLimit()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "attempt-reset@example.com", "StrongPass1", isEmailVerified: true);
        await SeedOtpAsync(dbContext, user, "333333", OtpPurposes.ResetPassword, DateTime.UtcNow.AddMinutes(5), attemptCount: 5);

        var authService = CreateAuthService(dbContext, otpService: CreateOtpService(dbContext));

        var result = await authService.ResetPasswordAsync(
            new ResetPasswordRequest
            {
                Email = user.Email,
                OtpCode = "333333",
                NewPassword = "NewStrongPass1",
                ConfirmPassword = "NewStrongPass1"
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OTP da vuot qua so lan thu cho phep.", result.Message);
    }

    [Fact]
    public async Task VerifyPasswordResetOtpAsync_ReturnsControlledFailure_WhenOtpPayloadIsCorrupted()
    {
        using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "corrupted-reset@example.com", "StrongPass1", isEmailVerified: true);
        dbContext.EmailVerificationOtps.Add(new EmailVerificationOtp
        {
            Email = user.Email,
            Purpose = OtpPurposes.ResetPassword,
            UserId = user.UserId,
            OtpHash = "not-base64",
            OtpSalt = "still-not-base64",
            AttemptCount = 0,
            IsUsed = false,
            RequestedIp = "127.0.0.1",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });
        await dbContext.SaveChangesAsync();

        var otpService = CreateOtpService(dbContext);
        var result = await otpService.VerifyPasswordResetOtpAsync(user.Email, "123456", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OTP không hợp lệ. Vui lòng yêu cầu mã mới.", result.Message);

        var savedOtp = await dbContext.EmailVerificationOtps.SingleAsync(x => x.UserId == user.UserId);
        Assert.True(savedOtp.IsUsed);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"otp-flow-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<User> SeedUserAsync(
        AppDbContext dbContext,
        string email,
        string password,
        bool isEmailVerified)
    {
        var role = new Role { RoleId = 1, RoleName = AppRoles.StandardUser };
        dbContext.Roles.Add(role);

        var user = new User
        {
            Username = email.Split('@')[0],
            FullName = "OTP Flow User",
            Email = email.ToLowerInvariant(),
            PasswordHash = PasswordHashUtility.HashPassword(password),
            RoleId = role.RoleId,
            IsLocked = false,
            IsEmailVerified = isEmailVerified,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private static async Task SeedRegisterOtpAsync(AppDbContext dbContext, User user, string otpCode, DateTime expiresAtUtc)
    {
        await SeedOtpAsync(dbContext, user, otpCode, OtpPurposes.Register, expiresAtUtc);
    }

    private static async Task SeedOtpAsync(
        AppDbContext dbContext,
        User user,
        string otpCode,
        string purpose,
        DateTime expiresAtUtc,
        int attemptCount = 0)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashOtp(otpCode, salt);

        dbContext.EmailVerificationOtps.Add(new EmailVerificationOtp
        {
            Email = user.Email,
            Purpose = purpose,
            UserId = user.UserId,
            OtpHash = hash,
            OtpSalt = salt,
            AttemptCount = attemptCount,
            IsUsed = false,
            RequestedIp = "127.0.0.1",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAtUtc
        });

        await dbContext.SaveChangesAsync();
    }

    private static EmailOtpService CreateOtpService(AppDbContext dbContext)
    {
        return new EmailOtpService(
            dbContext,
            new FakeEmailSender(),
            Options.Create(new EmailOtpSettings { ExpireMinutes = 10, MaxAttempts = 5 }),
            NullLogger<EmailOtpService>.Instance);
    }

    private static AuthService CreateAuthService(
        AppDbContext dbContext,
        IEmailOtpService? otpService = null,
        IEmailSender? emailSender = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var jwtSettings = new JwtSettings
        {
            Issuer = "SmartSpendAI.Tests",
            Audience = "SmartSpendAI.Tests.Client",
            SecretKey = "this-is-a-very-strong-test-secret-key-12345",
            AccessTokenMinutes = 60,
            RememberMeAccessTokenDays = 7
        };

        var signingMaterial = JwtSigningMaterial.Create(jwtSettings, Directory.GetCurrentDirectory());
        return new AuthService(
            dbContext,
            otpService ?? new NoopOtpService(),
            Options.Create(jwtSettings),
            signingMaterial,
            NullLogger<AuthService>.Instance,
            emailSender,
            httpContextAccessor);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(string remoteIp, string userAgent)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        httpContext.Request.Headers.UserAgent = userAgent;
        return new HttpContextAccessor { HttpContext = httpContext };
    }

    private static string HashOtp(string otpCode, string salt)
    {
        var input = Encoding.UTF8.GetBytes($"{otpCode}:{salt}");
        return Convert.ToBase64String(SHA256.HashData(input));
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("smtp unavailable");
        }
    }

    private sealed class NoopOtpService : IEmailOtpService
    {
        public Task<OtpDispatchResult> IssueRegisterOtpAsync(User user, string requestIp, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OtpDispatchResult { Success = true });
        }

        public Task<OtpVerificationResult> VerifyRegisterOtpAsync(string email, string otpCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OtpVerificationResult { Success = true });
        }

        public Task<OtpDispatchResult> ResendRegisterOtpAsync(string email, string requestIp, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OtpDispatchResult { Success = true });
        }

        public Task<OtpDispatchResult> IssuePasswordResetOtpAsync(User user, string requestIp, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OtpDispatchResult { Success = true });
        }

        public Task<OtpVerificationResult> VerifyPasswordResetOtpAsync(string email, string otpCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OtpVerificationResult { Success = true });
        }
    }
}
