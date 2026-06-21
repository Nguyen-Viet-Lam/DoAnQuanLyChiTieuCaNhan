using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSpendAI.Models;
using SmartSpendAI.Models.Dtos.Auth;
using SmartSpendAI.Security;
using SmartSpendAI.Services.Email;
using SmartSpendAI.Services.Otp;

namespace SmartSpendAI.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _dbContext;
        private readonly IEmailOtpService _emailOtpService;
        private readonly JwtSettings _jwtSettings;
        private readonly JwtSigningMaterial _jwtSigningMaterial;
        private readonly ILogger<AuthService> _logger;
        private readonly IEmailSender? _emailSender;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public AuthService(
            AppDbContext dbContext,
            IEmailOtpService emailOtpService,
            IOptions<JwtSettings> jwtSettings,
            JwtSigningMaterial jwtSigningMaterial,
            ILogger<AuthService> logger,
            IEmailSender? emailSender = null,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _dbContext = dbContext;
            _emailOtpService = emailOtpService;
            _jwtSettings = jwtSettings.Value;
            _jwtSigningMaterial = jwtSigningMaterial;
            _logger = logger;
            _emailSender = emailSender;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<LoginServiceResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        {
            var identifier = request.EmailOrUsername.Trim();
            var validationErrors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(identifier))
            {
                validationErrors[nameof(LoginRequest.EmailOrUsername)] = ["Email hoặc tên đăng nhập không được để trống."];
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                validationErrors[nameof(LoginRequest.Password)] = ["Mật khẩu không được để trống."];
            }

            if (validationErrors.Count > 0)
            {
                return new LoginServiceResult
                {
                    Success = false,
                    StatusCode = 400,
                    ValidationErrors = validationErrors
                };
            }

            var normalizedEmail = identifier.ToLowerInvariant();
            var user = await _dbContext.Users
                .AsNoTracking()
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Email == normalizedEmail || x.Username == identifier, cancellationToken);

            if (user is null || !PasswordHashUtility.VerifyPassword(request.Password, user.PasswordHash))
            {
                return new LoginServiceResult
                {
                    Success = false,
                    StatusCode = 401,
                    Message = "Email/tên đăng nhập hoặc mật khẩu không đúng."
                };
            }

            if (user.IsLocked)
            {
                return new LoginServiceResult
                {
                    Success = false,
                    StatusCode = 403,
                    Message = "Tài khoản đã bị khóa."
                };
            }

            if (!user.IsEmailVerified)
            {
                return new LoginServiceResult
                {
                    Success = false,
                    StatusCode = 403,
                    Message = "Email chưa được xác thực. Vui lòng nhập OTP trước khi đăng nhập."
                };
            }

            var now = DateTime.UtcNow;
            var lifetime = request.RememberMe
                ? TimeSpan.FromDays(Math.Max(1, _jwtSettings.RememberMeAccessTokenDays))
                : TimeSpan.FromMinutes(Math.Max(1, _jwtSettings.AccessTokenMinutes));
            var expiresAt = now.Add(lifetime);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role?.RoleName ?? AppRoles.StandardUser)
            };

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                notBefore: now,
                expires: expiresAt,
                signingCredentials: _jwtSigningMaterial.CreateSigningCredentials());

            _logger.LogInformation("User logged in successfully UserId={UserId}", user.UserId);
            await TrackLoginAndNotifyIfNewDeviceAsync(user, cancellationToken);

            return new LoginServiceResult
            {
                Success = true,
                StatusCode = 200,
                Response = new LoginResponse
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role?.RoleName ?? AppRoles.StandardUser,
                    AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                    ExpiresAt = expiresAt
                }
            };
        }

        public async Task<RegisterServiceResult> RegisterAsync(RegisterRequest request, string requestIp, CancellationToken cancellationToken)
        {
            var username = request.Username.Trim();
            var fullName = request.FullName.Trim();
            var email = request.Email.Trim().ToLowerInvariant();

            var validationErrors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(username))
            {
                validationErrors[nameof(RegisterRequest.Username)] = ["Username không được để trống."];
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                validationErrors[nameof(RegisterRequest.FullName)] = ["Họ tên không được để trống."];
            }

            if (!request.AcceptTerms)
            {
                validationErrors[nameof(RegisterRequest.AcceptTerms)] = ["Bạn cần đồng ý với điều khoản sử dụng."];
            }

            if (!HasStrongPassword(request.Password))
            {
                validationErrors[nameof(RegisterRequest.Password)] =
                    ["Mật khẩu cần có ít nhất 8 ký tự, gồm chữ hoa, chữ thường và chữ số."];
            }

            if (await _dbContext.Users.AnyAsync(x => x.Username == username, cancellationToken))
            {
                validationErrors[nameof(RegisterRequest.Username)] = ["Tên đăng nhập đã tồn tại."];
            }

            if (await _dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
            {
                validationErrors[nameof(RegisterRequest.Email)] = ["Email đã được sử dụng."];
            }

            if (validationErrors.Count > 0)
            {
                return new RegisterServiceResult
                {
                    Success = false,
                    ValidationErrors = validationErrors
                };
            }

            var user = new User
            {
                Username = username,
                FullName = fullName,
                Email = email,
                PasswordHash = PasswordHashUtility.HashPassword(request.Password),
                RoleId = await EnsureRoleIdAsync(AppRoles.StandardUser, cancellationToken),
                IsLocked = false,
                IsEmailVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                return new RegisterServiceResult
                {
                    Success = false,
                    IsConflict = true,
                    Message = "Tên đăng nhập hoặc email đã tồn tại."
                };
            }

            var otpDispatch = await _emailOtpService.IssueRegisterOtpAsync(user, requestIp, cancellationToken);

            return new RegisterServiceResult
            {
                Success = true,
                Response = new RegisterResponse
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    CreatedAt = user.CreatedAt,
                    IsEmailVerified = user.IsEmailVerified,
                    OtpDispatched = otpDispatch.Success,
                    OtpExpiresAt = otpDispatch.ExpiresAt,
                    Message = otpDispatch.Success
                        ? "Đăng ký thành công. Vui lòng kiểm tra email để nhập OTP xác thực."
                        : "Đăng ký thành công nhưng chưa gửi được OTP. Vui lòng gửi lại OTP."
                }
            };
        }

        public Task<OtpVerificationResult> VerifyEmailOtpAsync(VerifyEmailOtpRequest request, CancellationToken cancellationToken)
        {
            return _emailOtpService.VerifyRegisterOtpAsync(request.Email, request.OtpCode, cancellationToken);
        }

        public Task<OtpDispatchResult> ResendEmailOtpAsync(ResendEmailOtpRequest request, string requestIp, CancellationToken cancellationToken)
        {
            return _emailOtpService.ResendRegisterOtpAsync(request.Email, requestIp, cancellationToken);
        }

        public async Task<OtpDispatchResult> RequestPasswordResetAsync(
            ForgotPasswordRequest request,
            string requestIp,
            CancellationToken cancellationToken)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
            if (user is null)
            {
                return new OtpDispatchResult
                {
                    Success = true,
                    Message = "Nếu email tồn tại, hệ thống đã gửi mã OTP đặt lại mật khẩu."
                };
            }

            var dispatch = await _emailOtpService.IssuePasswordResetOtpAsync(user, requestIp, cancellationToken);
            return new OtpDispatchResult
            {
                Success = dispatch.Success,
                Message = dispatch.Success
                    ? "Hệ thống đã gửi mã OTP đặt lại mật khẩu."
                    : dispatch.Message,
                ExpiresAt = dispatch.ExpiresAt
            };
        }

        public async Task<SimpleServiceResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
        {
            var validationErrors = new Dictionary<string, string[]>();
            if (!HasStrongPassword(request.NewPassword))
            {
                validationErrors[nameof(ResetPasswordRequest.NewPassword)] =
                    ["Mật khẩu cần có ít nhất 8 ký tự, gồm chữ hoa, chữ thường và chữ số."];
            }

            if (validationErrors.Count > 0)
            {
                return new SimpleServiceResult
                {
                    Success = false,
                    ValidationErrors = validationErrors
                };
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
            if (user is null)
            {
                return new SimpleServiceResult
                {
                    Success = false,
                    Message = "Không tìm thấy tài khoản cần đặt lại mật khẩu."
                };
            }

            var verify = await _emailOtpService.VerifyPasswordResetOtpAsync(request.Email, request.OtpCode, cancellationToken);
            if (!verify.Success)
            {
                return new SimpleServiceResult
                {
                    Success = false,
                    Message = verify.Message
                };
            }

            user.PasswordHash = PasswordHashUtility.HashPassword(request.NewPassword);
            user.IsEmailVerified = true;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new SimpleServiceResult
            {
                Success = true,
                Message = "Đặt lại mật khẩu thành công."
            };
        }

        private async Task<int> EnsureRoleIdAsync(string roleName, CancellationToken cancellationToken)
        {
            var existingRole = await _dbContext.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RoleName == roleName, cancellationToken);

            if (existingRole is not null)
            {
                return existingRole.RoleId;
            }

            var role = new Role { RoleName = roleName };
            _dbContext.Roles.Add(role);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return role.RoleId;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _dbContext.Entry(role).State = EntityState.Detached;
                var fallback = await _dbContext.Roles.AsNoTracking().FirstAsync(x => x.RoleName == roleName, cancellationToken);
                return fallback.RoleId;
            }
        }

        private static bool HasStrongPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                return false;
            }

            var hasUpper = Regex.IsMatch(password, "[A-Z]");
            var hasLower = Regex.IsMatch(password, "[a-z]");
            var hasDigit = Regex.IsMatch(password, "[0-9]");

            return hasUpper && hasLower && hasDigit;
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        {
            return exception.InnerException is SqlException sqlException &&
                   (sqlException.Number == 2601 || sqlException.Number == 2627);
        }

        private async Task TrackLoginAndNotifyIfNewDeviceAsync(User user, CancellationToken cancellationToken)
        {
            try
            {
                var httpContext = _httpContextAccessor?.HttpContext;
                var requestIp = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "khong-ro-ip";
                var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
                var sanitizedUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "khong-ro-thiet-bi" : userAgent.Trim();
                var deviceFingerprint = BuildDeviceFingerprint(requestIp, sanitizedUserAgent);

                var previousLogin = await _dbContext.AuditLogs
                    .AsNoTracking()
                    .Where(x => x.ActorUserId == user.UserId && x.Action == "UserLoginSuccess")
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                var isNewDevice = previousLogin is not null &&
                                  !string.Equals(previousLogin.Metadata, deviceFingerprint, StringComparison.Ordinal);

                _dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = user.UserId,
                    Action = "UserLoginSuccess",
                    TargetType = "User",
                    TargetId = user.UserId.ToString(),
                    Metadata = deviceFingerprint,
                    CreatedAt = DateTime.UtcNow
                });

                await _dbContext.SaveChangesAsync(cancellationToken);

                if (isNewDevice && _emailSender is not null)
                {
                    var alertSent = await TrySendNewDeviceAlertAsync(
                        user,
                        requestIp,
                        sanitizedUserAgent,
                        cancellationToken);

                    if (alertSent)
                    {
                        _dbContext.AuditLogs.Add(new AuditLog
                        {
                            ActorUserId = user.UserId,
                            Action = "UserLoginNewDeviceAlertSent",
                            TargetType = "User",
                            TargetId = user.UserId.ToString(),
                            Metadata = deviceFingerprint,
                            CreatedAt = DateTime.UtcNow
                        });

                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể lưu audit đăng nhập hoặc gửi cảnh báo thiết bị lạ cho UserId={UserId}.", user.UserId);
            }
        }

        private static string BuildDeviceFingerprint(string requestIp, string userAgent)
        {
            return $"ip={requestIp};ua={userAgent}";
        }

        private async Task<bool> TrySendNewDeviceAlertAsync(
            User user,
            string requestIp,
            string sanitizedUserAgent,
            CancellationToken cancellationToken)
        {
            try
            {
                var issuedAt = DateTime.UtcNow;
                var subject = "Cảnh báo bảo mật: Phát hiện đăng nhập thiết bị lạ";
                var textBody =
                    $"Hệ thống phát hiện đăng nhập mới vào tài khoản {user.Email}.\n" +
                    $"Thời gian (UTC): {issuedAt:yyyy-MM-dd HH:mm:ss}\n" +
                    $"IP: {requestIp}\n" +
                    $"User-Agent: {sanitizedUserAgent}\n\n" +
                    "Nếu đây không phải bạn, vui lòng đổi mật khẩu ngay lập tức.";

                var htmlBody =
                    "<p>Hệ thống phát hiện <strong>đăng nhập mới</strong> vào tài khoản của bạn.</p>" +
                    $"<p><strong>Thời gian (UTC):</strong> {issuedAt:yyyy-MM-dd HH:mm:ss}<br/>" +
                    $"<strong>IP:</strong> {requestIp}<br/>" +
                    $"<strong>User-Agent:</strong> {sanitizedUserAgent}</p>" +
                    "<p>Nếu đây không phải bạn, vui lòng đổi mật khẩu ngay lập tức.</p>";

                await _emailSender!.SendAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể gửi cảnh báo thiết bị lạ cho UserId={UserId}.", user.UserId);
                return false;
            }
        }
    }
}
