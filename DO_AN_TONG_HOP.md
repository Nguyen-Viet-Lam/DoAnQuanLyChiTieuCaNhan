# DO AN TONG HOP - SmartSpend AI

Tai lieu nay tong hop:
- Do an lam duoc gi
- Kien truc va cong nghe su dung
- Cac file code quan trong + dong code chinh
- Phan chia cong viec cho 4 nguoi
- Cach chay du an
- Huong mo rong AI

## 1. Tom tat do an

SmartSpend AI la ung dung quan ly chi tieu ca nhan xay dung bang ASP.NET Core + SQL Server. Du an da co:
- Dang ky, dang nhap, xac thuc OTP email, quen mat khau, dat lai mat khau
- Quan ly vi, giao dich, ngan sach, danh muc, chuyen vi, xuat Excel
- Dashboard thong ke, insights, forecast, nhac nho AI
- Admin: quan ly nguoi dung, danh muc, audit log
- Giao dien web tinh o `wwwroot/home`
- Test tu dong cho nhieu phan chinh

## 2. Kien truc tong the

### 2.1 Entry point va cau hinh
- Khoi tao app, DI, auth, authorization, SignalR, seeding, map endpoint o [Program.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Program.cs#L22)
- `AddControllers`, `AddAuthentication`, `AddAuthorization`, `MapControllers`, `MapHub`, `MapStaticAssets` o [Program.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Program.cs#L29)
- Tu dong migrate/seed du lieu de chay local o [Program.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Program.cs#L155)

### 2.2 Lop chuc nang
- `Controllers`: API va MVC pages
- `Services`: business logic
- `Models`: entity, DTO, option
- `Validation`: FluentValidation
- `Security`: JWT, role, password hash, OTP purpose
- `wwwroot`: frontend tinh
- `SmartSpendAI.Tests`: unit test

## 3. Do an da lam duoc gi

### 3.1 Auth va Security
- Dang ky, dang nhap, verify OTP, resend OTP, quyen reset mat khau o [Services/Auth/AuthService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Auth\AuthService.cs#L45)
- `LoginAsync`, `RegisterAsync`, `RequestPasswordResetAsync`, `ResetPasswordAsync` o [Services/Auth/AuthService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Auth\AuthService.cs#L45)
- Controller auth o [Controllers/AuthController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\AuthController.cs#L11)
- Validation dang ky/dang nhap o [Validation/Auth/RegisterRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Auth\RegisterRequestValidator.cs#L6) va [Validation/Auth/LoginRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Auth\LoginRequestValidator.cs#L6)

### 3.2 Finance core
- CRUD vi o [Controllers/WalletsController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\WalletsController.cs#L12)
- CRUD giao dich o [Controllers/TransactionsController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\TransactionsController.cs#L16)
- CRUD ngan sach o [Controllers/BudgetsController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\BudgetsController.cs#L11)
- CRUD danh muc o [Controllers/CategoriesController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\CategoriesController.cs#L10)
- Export Excel o [Services/Finance/TransactionExportService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Finance\TransactionExportService.cs#L8)
- Validation finance o [Validation/Finance/WalletRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Finance\WalletRequestValidator.cs#L6), [Validation/Finance/TransactionRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Finance\TransactionRequestValidator.cs#L6), [Validation/Finance/BudgetRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Finance\BudgetRequestValidator.cs#L6)

### 3.3 Dashboard va report
- Dashboard tong hop o [Controllers/DashboardController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\DashboardController.cs#L29)
- Month trend o [Controllers/DashboardController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\DashboardController.cs#L114)
- Insights o [Controllers/DashboardController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\DashboardController.cs#L140)
- Forecast summary o [Controllers/DashboardController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\DashboardController.cs#L186)
- Forecast lines o [Controllers/DashboardController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\DashboardController.cs#L278)
- Report service o [Services/Reports/ReportService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Reports\ReportService.cs#L7)

### 3.4 AI features
- Smart input va hoc tu lich su o [Services/AI/SmartInputService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartInputService.cs#L37)
- Learn from correction o [Services/AI/SmartInputService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartInputService.cs#L223)
- Goi y tu lich su giao dich o [Services/AI/SmartInputService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartInputService.cs#L309)
- Tong hop nhac nho AI o [Services/AI/SmartReminderService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartReminderService.cs#L25)
- Du bao WMA o [Services/AI/SmartReminderService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartReminderService.cs#L317)
- Bat thuong chi tieu o [Services/AI/SmartReminderService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartReminderService.cs#L365)
- Goi y ngan sach ca nhan hoa o [Services/AI/SmartReminderService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\AI\SmartReminderService.cs#L447)

### 3.5 Admin va seeding
- Admin dashboard, user, log, category management o [Controllers/AdminController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\AdminController.cs#L12) va [Controllers/AdminCategoriesController.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Controllers\AdminCategoriesController.cs#L12)
- Seed du lieu demo o [Services/Setup/SmartSpendDataSeeder.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Setup\SmartSpendDataSeeder.cs#L8)

### 3.6 Email, OTP, background job
- Gui OTP email o [Services/Otp/EmailOtpService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Otp\EmailOtpService.cs#L11)
- Gui mail SMTP o [Services/Email/SmtpEmailSender.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Email\SmtpEmailSender.cs#L10)
- Weekly summary background service o [Services/Email/WeeklySummaryEmailHostedService.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Services\Email\WeeklySummaryEmailHostedService.cs#L7)

## 4. Database va entity

### 4.1 DbContext
- DbContext chinh o [Models/Entities/AppDbContext.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\AppDbContext.cs#L6)
- Design-time factory o [Models/Entities/AppDbContextFactory.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\AppDbContextFactory.cs#L7)

### 4.2 Entity chinh
- User o [Models/Entities/User.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\User.cs#L6)
- Wallet o [Models/Entities/Wallet.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\Wallet.cs#L5)
- TransactionEntry o [Models/Entities/TransactionEntry.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\TransactionEntry.cs#L5)
- Budget o [Models/Entities/Budget.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\Budget.cs#L5)
- Category o [Models/Entities/Category.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\Category.cs#L5)
- AuditLog o [Models/Entities/AuditLog.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\AuditLog.cs#L5)
- KeywordEntry o [Models/Entities/KeywordEntry.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\KeywordEntry.cs#L5)
- UserPersonalKeyword o [Models/Entities/UserPersonalKeyword.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\UserPersonalKeyword.cs#L6)
- EmailVerificationOtp o [Models/Entities/EmailVerificationOtp.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Entities\EmailVerificationOtp.cs#L5)

## 5. DTO va response

- DashboardResponse o [Models/Dtos/Dashboard/DashboardResponse.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Dashboard\DashboardResponse.cs#L5)
- ForecastSummaryDto o [Models/Dtos/Dashboard/ForecastSummaryDto.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Dashboard\ForecastSummaryDto.cs#L3)
- AiReminderResponse o [Models/Dtos/Dashboard/AiReminderResponse.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Dashboard\AiReminderResponse.cs#L3)
- ForecastBreakdownItemDto o [Models/Dtos/Dashboard/ForecastBreakdownItemDto.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Dashboard\ForecastBreakdownItemDto.cs#L3)
- TrendPointDto o [Models/Dtos/Dashboard/TrendPointDto.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Dashboard\TrendPointDto.cs#L3)
- CategoryBreakdownDto o [Models/Dtos/Dashboard/CategoryBreakdownDto.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Dashboard\CategoryBreakdownDto.cs#L3)
- BudgetRequest o [Models/Dtos/Finance/BudgetRequest.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Finance\BudgetRequest.cs#L5)
- BudgetResponse o [Models/Dtos/Finance/BudgetResponse.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Finance\BudgetResponse.cs#L3)
- TransactionRequest o [Models/Dtos/Finance/TransactionRequest.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Finance\TransactionRequest.cs#L5)
- WalletRequest o [Models/Dtos/Finance/WalletRequest.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Finance\WalletRequest.cs#L5)
- SmartInputResponse o [Models/Dtos/Finance/SmartInputResponse.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Models\Dtos\Finance\SmartInputResponse.cs#L3)

## 6. Validation

- Dang ky o [Validation/Auth/RegisterRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Auth\RegisterRequestValidator.cs#L6)
- Dang nhap o [Validation/Auth/LoginRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Auth\LoginRequestValidator.cs#L6)
- Wallet o [Validation/Finance/WalletRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Finance\WalletRequestValidator.cs#L6)
- Transaction o [Validation/Finance/TransactionRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Finance\TransactionRequestValidator.cs#L6)
- Budget o [Validation/Finance/BudgetRequestValidator.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\Validation\Finance\BudgetRequestValidator.cs#L6)

## 7. Giao dien web

### 7.1 Trang tinh
- Home o [wwwroot/home/index.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\index.html)
- Login o [wwwroot/home/login.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\login.html)
- Register o [wwwroot/home/register.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\register.html)
- Dashboard o [wwwroot/home/dashboard.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\dashboard.html)
- Transactions o [wwwroot/home/transactions.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\transactions.html)
- Budgets o [wwwroot/home/budgets.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\budgets.html)
- Wallets o [wwwroot/home/wallets.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\wallets.html)
- Reports o [wwwroot/home/reports.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\reports.html)
- Admin pages o [wwwroot/home/admin-dashboard.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\admin-dashboard.html), [wwwroot/home/admin-users.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\admin-users.html), [wwwroot/home/admin-categories.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\admin-categories.html), [wwwroot/home/admin-logs.html](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\home\admin-logs.html)

### 7.2 JS va CSS
- Dashboard AI render o [wwwroot/js/finance-pages.js](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\js\finance-pages.js#L315)
- AI reminders render o [wwwroot/js/finance-pages.js](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\js\finance-pages.js#L384)
- Init dashboard page o [wwwroot/js/finance-pages.js](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\js\finance-pages.js#L494)
- App shell o [wwwroot/js/app-shell.js](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\js\app-shell.js)
- Auth JS o [wwwroot/js/auth.js](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\js\auth.js)
- CSS chinh o [wwwroot/css/style.css](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\wwwroot\css\style.css)

## 8. Test

- Test forecast dashboard o [SmartSpendAI.Tests/Dashboard/DashboardForecastTests.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\SmartSpendAI.Tests\Dashboard\DashboardForecastTests.cs#L8)
- Test smart reminder o [SmartSpendAI.Tests/AI/SmartReminderServiceTests.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\SmartSpendAI.Tests\AI\SmartReminderServiceTests.cs#L7)
- Test smart input o [SmartSpendAI.Tests/AI/SmartInputServiceTests.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\SmartSpendAI.Tests\AI\SmartInputServiceTests.cs#L7)
- Test auth register o [SmartSpendAI.Tests/Auth/AuthServiceRegisterTests.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\SmartSpendAI.Tests\Auth\AuthServiceRegisterTests.cs#L12)
- Test auth otp flow o [SmartSpendAI.Tests/Auth/AuthOtpFlowTests.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\SmartSpendAI.Tests\Auth\AuthOtpFlowTests.cs#L16)
- Test weekly summary email o [SmartSpendAI.Tests/Email/WeeklySummaryEmailHostedServiceTests.cs](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\SmartSpendAI.Tests\Email\WeeklySummaryEmailHostedServiceTests.cs#L11)

## 9. Cong nghe va kien thuc mon Cong nghe phan mem

Du an dung:
- Phan tich yeu cau va chia module
- Thiet ke kien truc lop
- REST API
- Entity Framework Core + migrations
- SQL Server
- JWT authentication va role-based authorization
- FluentValidation
- SignalR
- Background service
- Unit testing
- Frontend tinh + JavaScript

## 10. Chia cong viec cho 4 nguoi

### Tuan 1
- Lam: Dang ky, dang nhap
- Ky: Thiet ke DB, entity, migration
- Loi: Trang home, layout chung
- Thuc: Cau hinh project, README, chay app

### Tuan 2
- Lam: OTP email, quen mat khau
- Ky: Vi va giao dich
- Loi: Danh muc va ngan sach
- Thuc: Validation va form JS

### Tuan 3
- Lam: Phan quyen, JWT, admin co ban
- Ky: Chuyen tien, export Excel
- Loi: Dashboard thong ke
- Thuc: Hoan thien giao dien cac trang chinh

### Tuan 4
- Lam: Phat hien bat thuong chi tieu v1
- Ky: Chuan bi du lieu forecast
- Loi: Goi y ngan sach v1
- Thuc: Noi AI vao dashboard

### Tuan 5
- Lam: Cai thien anomaly, giam bao sai
- Ky: Du bao ngan sach tuan/thang v1
- Loi: Ca nhan hoa ngan sach theo lich su
- Thuc: Lam UI hien thi dep hon

### Tuan 6
- Lam: Test anomaly
- Ky: Test forecast
- Loi: Test recommendation
- Thuc: Seed du lieu demo

### Tuan 7
- Lam: Sua auth/admin
- Ky: Sua finance/report
- Loi: Sua AI recommendation
- Thuc: Ral UI, responsive, text bao cao

### Tuan 8
- Lam: Chuan bi demo auth + security
- Ky: Chuan bi demo finance + forecast
- Loi: Chuan bi demo AI + recommendation
- Thuc: Tong hop report, anh, slide, chay test cuoi

## 11. AI features hien tai va huong phat trien

Da co:
- Phat hien bat thuong chi tieu
- Du bao ngan sach
- Goi y ngan sach ca nhan hoa

Huong nang cap:
- Giu baseline chi tieu binh thuong theo nguong thuc te
- Thay WMA bang model don gian co so sanh theo tuan/thang
- Giai thich vi sao AI dua ra goi y bang ngan nguon du lieu
- Boi them OCR hoa don, chat assistant, va phat hien bat thuong nang cao

## 12. Cach chay

### Local
1. Dam bao SQL Server dang chay
2. Kiem tra [appsettings.json](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\appsettings.json) va [appsettings.Local.json](C:\Users\ASUS\Downloads\QuanLyChiTieuCaNhan\appsettings.Local.json)
3. Chay migrate
4. Chay app:
```powershell
dotnet run --launch-profile http
```

### Test
```powershell
dotnet test SmartSpendAI.sln
```

## 13. Ghi chu ve cau hinh

- `appsettings.json`: cau hinh mac dinh cua du an
- `appsettings.Local.json`: cau hinh rieng khi chay local
- `appsettings.Local.example.json`: mau de copy neu can
- `appsettings.Development.json`: cau hinh theo moi truong Development

## 14. Tong ket

Du an da co day du cac phan chinh de lam bao cao mon cong nghe phan mem:
- Co yeu cau nghiep vu
- Co database
- Co backend, frontend, validation, test
- Co AI mo rong
- Co phan chia cong viec ro rang cho nhom 4 nguoi

