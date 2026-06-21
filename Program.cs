using System.Security.Claims;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSpendAI.Models;
using SmartSpendAI.Security;
using SmartSpendAI.Services.AI;
using SmartSpendAI.Services.Auth;
using SmartSpendAI.Services.Email;
using SmartSpendAI.Services.Finance;
using SmartSpendAI.Services.Otp;
using SmartSpendAI.Services.Realtime;
using SmartSpendAI.Services.Reports;
using SmartSpendAI.Services.Setup;
using SmartSpendAI.Services.Users;
using SmartSpendAI.Validation.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddSignalR();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".keys")))
    .SetApplicationName("SmartSpendAI");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<EmailOtpSettings>(builder.Configuration.GetSection("EmailOtp"));
builder.Services.Configure<WeeklySummaryEmailSettings>(builder.Configuration.GetSection("WeeklySummaryEmail"));
builder.Services.Configure<SmartSpendSeedOptions>(builder.Configuration.GetSection("SmartSpendSeed"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    jwtSettings.SecretKey = "CHANGE_THIS_TO_A_STRONG_SECRET_KEY_AT_LEAST_32_CHARS";
}

var jwtSigningMaterial = JwtSigningMaterial.Create(jwtSettings, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(jwtSigningMaterial);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = jwtSigningMaterial.ValidationKey,
            ValidAlgorithms = [jwtSigningMaterial.Algorithm],
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/budget-alerts"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.UserOrAdmin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
            return AppRoles.IsUser(role) || AppRoles.IsAdmin(role);
        });
    });

    options.AddPolicy(AppPolicies.AdminOnly, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
            return AppRoles.IsAdmin(role);
        });
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IEmailOtpService, EmailOtpService>();
builder.Services.AddScoped<ITransactionExportService, TransactionExportService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISmartInputService, SmartInputService>();
builder.Services.AddScoped<ISmartReminderService, SmartReminderService>();
builder.Services.AddScoped<ISmartSpendDataSeeder, SmartSpendDataSeeder>();
builder.Services.AddHostedService<WeeklySummaryEmailHostedService>();

var app = builder.Build();

if (jwtSigningMaterial.IsAsymmetric && jwtSigningMaterial.IsEphemeral)
{
    app.Logger.LogWarning("JWT bất đối xứng đang dùng cặp khóa sinh tự động trong bộ nhớ. Token sẽ mất hiệu lực sau khi ứng dụng khởi động lại.");
}

var smtpSettings = app.Services.GetRequiredService<IOptions<SmtpSettings>>().Value;
if (string.IsNullOrWhiteSpace(smtpSettings.Host))
{
    app.Logger.LogWarning("SMTP chưa có Host. Không thể gửi email OTP. Hãy cấu hình Smtp:Host, Smtp:Username và Smtp:Password trong appsettings.Local.json.");
}
else if (smtpSettings.UseOAuth2)
{
    app.Logger.LogWarning("Luồng OTP hiện tại chỉ hỗ trợ SMTP theo tài khoản/mật khẩu. Hãy đặt Smtp:UseOAuth2=false nếu chưa triển khai bộ gửi OAuth2.");
}
else if (string.IsNullOrWhiteSpace(smtpSettings.Username) ||
         string.IsNullOrWhiteSpace(smtpSettings.Password) ||
         string.IsNullOrWhiteSpace(smtpSettings.FromEmail) ||
         LooksLikePlaceholder(smtpSettings.Username) ||
         LooksLikePlaceholder(smtpSettings.Password) ||
         LooksLikePlaceholder(smtpSettings.FromEmail))
{
    app.Logger.LogWarning("Thiếu thông tin xác thực SMTP. Hãy cấu hình Smtp:Username, Smtp:Password và Smtp:FromEmail.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

await EnsureSmartSpendSetupAsync(app);

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<BudgetAlertsHub>("/hubs/budget-alerts");
app.MapStaticAssets();

app.MapGet("/", () => Results.Redirect("/home/index.html"));
app.MapGet("/home", () => Results.Redirect("/home/index.html"));
app.MapGet("/error", () => Results.Problem("Đã xảy ra lỗi không mong muốn."));

app.Run();

static async Task EnsureSmartSpendSetupAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<SmartSpendSeedOptions>>().Value;
    if (!options.AutoApplyMigrations && !options.Enabled)
    {
        return;
    }

    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (options.AutoApplyMigrations)
        {
            await ApplyMigrationsWithRecoveryAsync(app, dbContext, options);
            app.Logger.LogInformation("Đã áp dụng migration SmartSpend thành công.");
        }

        if (options.Enabled)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<ISmartSpendDataSeeder>();
            await seeder.SeedAsync(CancellationToken.None);
            app.Logger.LogInformation("Đã hoàn tất seed dữ liệu phát triển SmartSpend.");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Bỏ qua bước thiết lập SmartSpend vì không thể hoàn tất migration hoặc seed cơ sở dữ liệu.");
    }
}

static async Task ApplyMigrationsWithRecoveryAsync(
    WebApplication app,
    AppDbContext dbContext,
    SmartSpendSeedOptions options)
{
    try
    {
        await dbContext.Database.MigrateAsync();
    }
    catch (Exception ex) when (options.RecreateDatabaseOnMigrationFailure)
    {
        app.Logger.LogWarning(ex, "Migration thất bại. Đang tạo lại cơ sở dữ liệu phát triển SmartSpend từ đầu.");
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }
}

static bool LooksLikePlaceholder(string? value)
{
    var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(normalized))
    {
        return false;
    }

    return normalized.Contains("replace-with") ||
           normalized.Contains("your-email") ||
           normalized.Contains("your-gmail-app-password");
}
