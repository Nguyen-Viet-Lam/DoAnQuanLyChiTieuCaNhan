using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSpendAI.Models;
using SmartSpendAI.Security;
using SmartSpendAI.Services.Email;

namespace SmartSpendAI.Services.Otp
{
    public class EmailOtpService : IEmailOtpService
    {
        private readonly AppDbContext _dbContext;
        private readonly IEmailSender _emailSender;
        private readonly EmailOtpSettings _otpSettings;
        private readonly ILogger<EmailOtpService> _logger;

        public EmailOtpService(
            AppDbContext dbContext,
            IEmailSender emailSender,
            IOptions<EmailOtpSettings> otpOptions,
            ILogger<EmailOtpService> logger)
        {
            _dbContext = dbContext;
            _emailSender = emailSender;
            _otpSettings = otpOptions.Value;
            _logger = logger;
        }

        public Task<OtpDispatchResult> IssueRegisterOtpAsync(User user, string requestIp, CancellationToken cancellationToken)
        {
            return IssueOtpAsync(user, requestIp, OtpPurposes.Register, cancellationToken);
        }

        public Task<OtpVerificationResult> VerifyRegisterOtpAsync(string email, string otpCode, CancellationToken cancellationToken)
        {
            return VerifyOtpAsync(email, otpCode, OtpPurposes.Register, updateEmailVerification: true, cancellationToken);
        }

        public async Task<OtpDispatchResult> ResendRegisterOtpAsync(string email, string requestIp, CancellationToken cancellationToken)
        {
            var normalizedEmail = NormalizeEmail(email);
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
            if (user is null)
            {
                return new OtpDispatchResult
                {
                    Success = false,
                    Message = "Email chưa đăng ký tài khoản."
                };
            }

            if (user.IsEmailVerified)
            {
                return new OtpDispatchResult
                {
                    Success = false,
                    Message = "Email đã được xác thực."
                };
            }

            return await IssueRegisterOtpAsync(user, requestIp, cancellationToken);
        }

        public Task<OtpDispatchResult> IssuePasswordResetOtpAsync(User user, string requestIp, CancellationToken cancellationToken)
        {
            return IssueOtpAsync(user, requestIp, OtpPurposes.ResetPassword, cancellationToken);
        }

        public Task<OtpVerificationResult> VerifyPasswordResetOtpAsync(string email, string otpCode, CancellationToken cancellationToken)
        {
            return VerifyOtpAsync(email, otpCode, OtpPurposes.ResetPassword, updateEmailVerification: false, cancellationToken);
        }

        private async Task<OtpDispatchResult> IssueOtpAsync(User user, string requestIp, string purpose, CancellationToken cancellationToken)
        {
            var email = NormalizeEmail(user.Email);
            var now = DateTime.UtcNow;
            var expiresAt = now.AddMinutes(Math.Max(_otpSettings.ExpireMinutes, 1));

            var activeOtps = await _dbContext.EmailVerificationOtps
                .Where(x => x.Email == email && x.Purpose == purpose && !x.IsUsed)
                .ToListAsync(cancellationToken);

            foreach (var activeOtp in activeOtps)
            {
                activeOtp.IsUsed = true;
                activeOtp.UsedAt = now;
            }

            var otpCode = GenerateOtpCode();
            var salt = GenerateSalt();
            var hash = HashOtp(otpCode, salt);

            _dbContext.EmailVerificationOtps.Add(new EmailVerificationOtp
            {
                Email = email,
                Purpose = purpose,
                UserId = user.UserId,
                OtpHash = hash,
                OtpSalt = salt,
                AttemptCount = 0,
                IsUsed = false,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                RequestedIp = requestIp
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                await SendOtpMailAsync(email, otpCode, expiresAt, purpose, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email for {Purpose} to {Email}", purpose, email);
                return new OtpDispatchResult
                {
                    Success = false,
                    Message = "Tao OTP thanh cong nhung gui email that bai.",
                    ExpiresAt = expiresAt
                };
            }

            return new OtpDispatchResult
            {
                Success = true,
                Message = "Da gui ma OTP toi email.",
                ExpiresAt = expiresAt
            };
        }

        private async Task<OtpVerificationResult> VerifyOtpAsync(
            string email,
            string otpCode,
            string purpose,
            bool updateEmailVerification,
            CancellationToken cancellationToken)
        {
            var normalizedEmail = NormalizeEmail(email);
            var now = DateTime.UtcNow;
            var maxAttempts = Math.Max(_otpSettings.MaxAttempts, 1);

            var otpEntity = await _dbContext.EmailVerificationOtps
                .Where(x => x.Email == normalizedEmail && x.Purpose == purpose && !x.IsUsed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (otpEntity is null)
            {
                return new OtpVerificationResult
                {
                    Success = false,
                    Message = "Không tìm thấy OTP hợp lệ."
                };
            }

            if (otpEntity.ExpiresAt <= now)
            {
                otpEntity.IsUsed = true;
                otpEntity.UsedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new OtpVerificationResult
                {
                    Success = false,
                    Message = "OTP da het han."
                };
            }

            if (otpEntity.AttemptCount >= maxAttempts)
            {
                otpEntity.IsUsed = true;
                otpEntity.UsedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new OtpVerificationResult
                {
                    Success = false,
                    Message = "OTP da vuot qua so lan thu cho phep."
                };
            }

            bool isOtpValid;
            try
            {
                isOtpValid = VerifyOtp(otpCode, otpEntity.OtpSalt, otpEntity.OtpHash);
            }
            catch (Exception ex)
            {
                otpEntity.IsUsed = true;
                otpEntity.UsedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "OTP payload is invalid for {Purpose} {Email}.", purpose, normalizedEmail);
                return new OtpVerificationResult
                {
                    Success = false,
                    Message = "OTP không hợp lệ. Vui lòng yêu cầu mã mới."
                };
            }

            if (!isOtpValid)
            {
                otpEntity.AttemptCount += 1;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new OtpVerificationResult
                {
                    Success = false,
                    Message = "OTP không chính xác."
                };
            }

            otpEntity.IsUsed = true;
            otpEntity.UsedAt = now;

            if (updateEmailVerification)
            {
                var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
                if (user is not null)
                {
                    user.IsEmailVerified = true;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new OtpVerificationResult
            {
                Success = true,
                Message = purpose == OtpPurposes.Register
                    ? "Xac thuc email thanh cong."
                    : "OTP đặt lại mật khẩu hợp lệ."
            };
        }

        private async Task SendOtpMailAsync(
            string email,
            string otpCode,
            DateTime expiresAt,
            string purpose,
            CancellationToken cancellationToken)
        {
            var subject = purpose == OtpPurposes.Register
                ? "SmartSpend - Mã OTP xác thực tài khoản"
                : "SmartSpend - Mã OTP đặt lại mật khẩu";

            var title = purpose == OtpPurposes.Register ? "xác thực tài khoản" : "đặt lại mật khẩu";
            var textBody = $"Ma OTP de {title} cua ban la: {otpCode}. Ma co hieu luc den {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.";
            var htmlBody = $"<p>Ma OTP de {title} cua ban la:</p><h2>{otpCode}</h2><p>Ma co hieu luc den <strong>{expiresAt:yyyy-MM-dd HH:mm:ss} UTC</strong>.</p>";

            await _emailSender.SendAsync(email, subject, htmlBody, textBody, cancellationToken);
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private static string GenerateOtpCode()
        {
            return RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
        }

        private static string GenerateSalt()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        }

        private static string HashOtp(string otpCode, string salt)
        {
            var input = Encoding.UTF8.GetBytes($"{otpCode}:{salt}");
            return Convert.ToBase64String(SHA256.HashData(input));
        }

        private static bool VerifyOtp(string otpCode, string salt, string storedHash)
        {
            var computedHash = Convert.FromBase64String(HashOtp(otpCode, salt));
            var storedBytes = Convert.FromBase64String(storedHash);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedBytes);
        }
    }
}
