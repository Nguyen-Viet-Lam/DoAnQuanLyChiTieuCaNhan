using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Auth;
using SmartSpendAI.Security;
using SmartSpendAI.Services.Auth;
using SmartSpendAI.Services.Otp;

namespace SmartSpendAI.Tests.Auth;

public sealed class AuthServiceRegisterTests
{
    [Fact]
    public async Task RegisterAsync_ReturnsValidationErrors_WhenRequiredFieldsAreInvalid()
    {
        using var dbContext = CreateDbContext();
        var otpService = new FakeEmailOtpService();
        var service = CreateService(dbContext, otpService);

        var request = new RegisterRequest
        {
            Username = " ",
            FullName = " ",
            Email = "student@example.com",
            Password = "weak",
            ConfirmPassword = "weak",
            AcceptTerms = false
        };

        var result = await service.RegisterAsync(request, "127.0.0.1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(nameof(RegisterRequest.Username), result.ValidationErrors.Keys);
        Assert.Contains(nameof(RegisterRequest.FullName), result.ValidationErrors.Keys);
        Assert.Contains(nameof(RegisterRequest.AcceptTerms), result.ValidationErrors.Keys);
        Assert.Contains(nameof(RegisterRequest.Password), result.ValidationErrors.Keys);
        Assert.Empty(dbContext.Users);
        Assert.Equal(0, otpService.IssueCallCount);
    }

    [Fact]
    public async Task RegisterAsync_CreatesUserAndDispatchesOtp_WhenRequestIsValid()
    {
        using var dbContext = CreateDbContext();
        var otpService = new FakeEmailOtpService
        {
            NextDispatchResult = new OtpDispatchResult
            {
                Success = true,
                ExpiresAt = new DateTime(2030, 1, 1, 0, 5, 0, DateTimeKind.Utc)
            }
        };

        var service = CreateService(dbContext, otpService);
        var request = CreateValidRequest();

        var result = await service.RegisterAsync(request, "203.0.113.5", CancellationToken.None);

        Assert.True(result.Success);
        var response = Assert.IsType<RegisterResponse>(result.Response);
        Assert.Equal("new.user", response.Username);
        Assert.Equal("new.user@example.com", response.Email);
        Assert.True(response.OtpDispatched);
        Assert.Equal(new DateTime(2030, 1, 1, 0, 5, 0, DateTimeKind.Utc), response.OtpExpiresAt);

        Assert.Equal(1, otpService.IssueCallCount);
        var savedUser = await dbContext.Users.SingleAsync();
        Assert.StartsWith("PBKDF2$SHA256$", savedUser.PasswordHash, StringComparison.Ordinal);
        Assert.Single(dbContext.Roles.Where(x => x.RoleName == AppRoles.StandardUser));
    }

    [Fact]
    public async Task RegisterAsync_ReturnsSuccessWithWarningMessage_WhenOtpDispatchFails()
    {
        using var dbContext = CreateDbContext();
        var otpService = new FakeEmailOtpService
        {
            NextDispatchResult = new OtpDispatchResult
            {
                Success = false,
                Message = "smtp unavailable",
                ExpiresAt = null
            }
        };

        var service = CreateService(dbContext, otpService);

        var result = await service.RegisterAsync(CreateValidRequest(), "127.0.0.1", CancellationToken.None);

        Assert.True(result.Success);
        var response = Assert.IsType<RegisterResponse>(result.Response);
        Assert.False(response.OtpDispatched);
        Assert.Equal("Đăng ký thành công nhưng chưa gửi được OTP. Vui lòng gửi lại OTP.", response.Message);
        Assert.Single(dbContext.Users);
    }

    private static AuthService CreateService(AppDbContext dbContext, FakeEmailOtpService otpService)
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
        return new AuthService(dbContext, otpService, Options.Create(jwtSettings), signingMaterial, NullLogger<AuthService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"register-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static RegisterRequest CreateValidRequest()
    {
        return new RegisterRequest
        {
            Username = "new.user",
            FullName = "New User",
            Email = "new.user@example.com",
            Password = "StrongPass1",
            ConfirmPassword = "StrongPass1",
            AcceptTerms = true
        };
    }

    private sealed class FakeEmailOtpService : IEmailOtpService
    {
        public int IssueCallCount { get; private set; }

        public OtpDispatchResult NextDispatchResult { get; set; } = new()
        {
            Success = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        public Task<OtpDispatchResult> IssueRegisterOtpAsync(User user, string requestIp, CancellationToken cancellationToken)
        {
            IssueCallCount++;
            return Task.FromResult(NextDispatchResult);
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
