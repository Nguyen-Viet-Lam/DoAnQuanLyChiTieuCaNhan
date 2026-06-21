# SmartSpend AI

SmartSpend AI is an ASP.NET Core + SQL Server personal finance app for demo and product iteration.

## Core Scope (Today)
- Light-first UI with dark toggle persistence.
- Wallet, transaction, budget, dashboard, profile, admin pages.
- OTP email verification, JWT auth, SignalR budget alerts.
- AI smart input with personal keyword learning.
- Transaction export to Excel (`.xlsx`) with existing filters.
- Local-only workflow (no Docker in this scope).

## Project Names
- Solution: `SmartSpendAI.sln`
- App project: `SmartSpendAI.csproj`
- Test project: `SmartSpendAI.Tests/SmartSpendAI.Tests.csproj`

## Project Structure
- `Controllers`: HTTP APIs by feature (`Auth`, `Profile`, `Finance`, `Admin`, `Dashboard`, `AI`).
- `Services`: business logic split by domain (`Auth`, `Otp`, `Email`, `AI`, `Finance`, `User`, `Realtime`, `Setup`).
- `Models`: `Dtos`, `Entities`, `Options`.
- `Security`: JWT signing, role constants, password hashing, OTP purposes.

## Auth Flow (Core)
- Register: `POST /api/auth/register` creates account with `IsEmailVerified=false`.
- OTP verify: `POST /api/auth/verify-email-otp` updates `IsEmailVerified=true`.
- Resend OTP: `POST /api/auth/resend-email-otp` for unverified users.
- Login: `POST /api/auth/login` is blocked until email is verified.
- Role-based access:
- `SystemAdmin`: admin endpoints.
- `StandardUser`: finance/profile flows.

## Database
Main tables include:
- `Users`, `Roles`, `EmailVerificationOtps`
- `Wallets`, `Transactions`, `Budgets`, `BudgetAlerts`, `Transfers`
- `Categories`, `Keywords`, `UserPersonalKeywords`
- `AuditLogs`

Latest migrations:
- `InitialSmartSpend`
- `AddUserPersonalKeywords`

## Run Local (No Docker)
1. Ensure SQL Server is available (example: `.\SQLEXPRESS`).
2. Check `ConnectionStrings:DefaultConnection` in `appsettings.json`.
3. Optional local override:
- create `appsettings.Local.json` from `appsettings.Local.example.json`
4. Configure SMTP for OTP in `appsettings.Local.json` (admin sender):
```json
"Smtp": {
  "Host": "smtp.your-provider.com",
  "Port": 587,
  "EnableSsl": true,
  "UseOAuth2": false,
  "Username": "admin@smartspend.local",
  "Password": "replace-with-smtp-password",
  "FromEmail": "admin@smartspend.local",
  "FromName": "SmartSpend Admin"
}
```
5. Fill `Smtp:Host`, `Smtp:Username`, and `Smtp:Password` with your mail provider credentials.
6. Apply migrations:
```powershell
dotnet ef database update --project SmartSpendAI.csproj --startup-project SmartSpendAI.csproj
```
7. Run app:
```powershell
dotnet run --launch-profile http
```

## Main URLs
- Home: `http://localhost:5176/home/index.html`
- Login: `http://localhost:5176/home/login.html`
- Dashboard: `http://localhost:5176/home/dashboard.html`
- Transactions: `http://localhost:5176/home/transactions.html`
- Admin: `http://localhost:5176/home/admin-dashboard.html`

## New Core APIs
- `POST /api/ai/smart-input`
- `POST /api/ai/learn-from-correction`
- `GET /api/transactions/export`

## Notes
- Timezone default for planning/report references: `Asia/Bangkok`.
- Keep current URL/page map for demo stability.
- Enterprise extensions (Hangfire, anomaly login, etc.) are out of today scope.
