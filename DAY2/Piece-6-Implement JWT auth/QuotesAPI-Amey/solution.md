# Piece-6 Solution Submission - JWT Auth

## Objective
Implement minimal JWT authentication in Quotes API so quote write operations require a valid bearer token while read operations stay public.

## Deliverables Completed

### 1) Auth login endpoint
- Added `POST /api/auth/login`
- Accepts JSON `{ email, password }`
- Validates credentials using BCrypt
- Returns:
  - `access_token`
  - `refresh_token`
  - `expires_in`

### 2) Users table and migration
- Added `User` model with:
  - `Id: Guid`
  - `Email: string`
  - `PasswordHash: string`
- Updated `QuoteDbContext` with `DbSet<User>`
- Added model rules:
  - required email
  - unique email index
  - required password hash
- Created migration: `20260520111237_AddUsersTable`

### 3) BCrypt password handling
- Installed `BCrypt.Net-Next`
- Login verifies hashed password via `BCrypt.Net.BCrypt.Verify(...)`
- No plain-text password storage

### 4) JWT wiring in Program.cs
- Installed `Microsoft.AspNetCore.Authentication.JwtBearer`
- Added:
  - `AddAuthentication().AddJwtBearer(...)`
  - `AddAuthorization()`
  - `UseAuthentication()` and `UseAuthorization()`
- JWT settings:
  - HS256 signing
  - Key from `IConfiguration` (`Jwt:Key`)
  - Enforced minimum 256-bit key
  - `ClockSkew = TimeSpan.Zero`

### 5) Endpoint authorization
- Protected:
  - `POST /api/quotes`
  - `DELETE /api/quotes/{id}`
- Kept open:
  - `GET /api/quotes`
  - `GET /api/quotes/{id}`

### 6) Evidence tests (curl)
`curl_output.txt` contains captured responses for:
1. POST without token -> `401 Unauthorized`
2. POST with valid token -> success (`201 Created` from current endpoint behavior)
3. POST with expired token -> `401 Unauthorized` and `WWW-Authenticate: Bearer error="invalid_token"...`

## Files Changed
- `Program.cs`
- `QuotesApi.csproj`
- `Data/QuoteDbContext.cs`
- `Extensions/ServiceCollectionExtensions.cs`
- `Models/User.cs`
- `appsettings.json`
- `appsettings.Development.json`
- `Migrations/20260520111237_AddUsersTable.cs`
- `Migrations/20260520111237_AddUsersTable.Designer.cs`
- `Migrations/QuoteDbContextModelSnapshot.cs`
- `README.md`
- `solution.md`
- `curl_output.txt`

## Run Commands Used
```bash
dotnet ef migrations add AddUsersTable
dotnet build
dotnet run --urls http://localhost:5000
```

## Login Test User
- Email: `user@test.com`
- Password: `password123`

This user is seeded automatically on startup if no users exist (for local test convenience).
