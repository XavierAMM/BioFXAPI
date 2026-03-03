# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build the solution
dotnet build BioFXAPI/BioFXAPI.sln

# Run the API (from repo root)
dotnet run --project BioFXAPI/BioFXAPI

# Run with hot reload
dotnet watch --project BioFXAPI/BioFXAPI run
```

There are no automated tests in this project.

Swagger UI is only available in Development mode at `/swagger`.

## Project Structure

Single ASP.NET Core 8.0 Web API project at `BioFXAPI/BioFXAPI/`:
- `Controllers/` — API endpoint controllers
- `Services/` — business logic and background services
- `Models/` — entity and DTO classes
- `Options/` — strongly-typed configuration classes (e.g., `S3Options`)
- `Notifications/` — `OrderNotificationService` (referenced via `BioFXAPI.Notifications` namespace)
- `SQL/` — database creation scripts and maintenance queries
- `Logs/` — rolling daily log files (via Serilog)

## Architecture Overview

### Database Access
Uses **raw ADO.NET (`SqlConnection`) + Dapper** for all queries. There is no EF Core DbContext in use despite EF packages being present. All SQL is inline in controllers and services. Transactions are managed manually with `SqlTransaction`.

### Authentication
JWT Bearer tokens are issued at login and stored as **HttpOnly cookies** named `biofx_auth` (not in Authorization headers). The cookie is `SameSite=None; Secure`. Token lifetime is 24 hours. `ClockSkew` is zero.

### Payment Flow (PlacetoPay)
1. Client calls `POST /api/Orders/create` → creates order with `PENDING` status, reserves stock
2. Client calls `POST /api/Orders/{id}/placetopay/session` → creates PlacetoPay session, returns `processUrl`
3. PlacetoPay sends webhook to `POST /api/WebhookLogs/placetopay`
4. Webhook handler immediately calls `POST /api/Transactions/internal/refresh-by-request` (authenticated with `X-Internal-Api-Key` header) which invokes `PlacetoPayRefreshService`
5. `PlacetoPayRefreshService.RefreshByRequestIdAsync()` queries PlacetoPay's `api/session/{requestId}`, computes the target status using payment-priority logic, applies idempotency guards, updates stock and sends email notifications on first `PAID` transition

Order/Transaction status values: `PENDING`, `PENDING_VALIDATION`, `PAID`, `REJECTED`, `EXPIRED`, `CANCELLED`.

**Idempotency rule**: `PAID` is never overwritten. `CANCELLED`/`EXPIRED` are never revived. Use `ApplyFinalStateIdempotency()` in `PlacetoPayRefreshService` as the source of truth.

### Background Services
- `AccountMaintenanceHostedService` — runs daily (configurable); disables unconfirmed accounts after 7 days, cleans up expired tokens after 30 days. Uses `sp_getapplock` as a distributed lock.
- `PlacetoPayAutoRefreshHostedService` — periodically batch-refreshes PENDING transactions by calling `PlacetoPayRefreshService.RefreshPendingBatchAsync()`.

### File Storage
Prescription attachments (`tieneReceta`) are uploaded to **AWS S3** via `IFileStorageService` / `S3FileStorageService`. After a paid order notification email is sent, the attachment is deleted from S3 and the DB reference is cleared.

### Email
Uses **MailKit** for SMTP. `EmailService` is a singleton that retries up to 3 times. Port 465 uses `SslOnConnect`; other ports use `StartTLS`.

## Configuration

Sensitive values **must** come from User Secrets (`dotnet user-secrets`) or environment variables — they are not in `appsettings.json`:
- `EmailSettings:SenderPassword` — SMTP password
- `PlacetoPay:SecretKey` (production value)
- `PlacetoPay:Login` (production value)
- `AwsS3:AccessKeyId`, `AwsS3:SecretAccessKey`
- `Jwt:Secret`

Key `appsettings.json` sections:
| Section | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection |
| `Jwt` | JWT signing secret, issuer, audience |
| `EmailSettings` | SMTP server, port, sender email |
| `PlacetoPay` | Gateway credentials, URLs, timeouts |
| `AwsS3` | Bucket and region for attachments |
| `InternalApiKey` | Shared secret for internal webhook processing |
| `Webhook:InternalApiBaseUrl` | Base URL for self-calls (`api.biofx.com.ec`) |
| `Frontend:BaseUrl` | Frontend origin for email links |
| `AllowedOrigins` | CORS whitelist |
| `OrderNotifications` | Shipping email recipients, low-stock threshold (default 5) |
| `AccountMaintenance` | Maintenance intervals in minutes |

## Rate Limiting

Three policies applied at middleware level:
- `api` (global, all controllers): 60 req/min
- `StrictLogin` (`POST /api/Auth/login`): 5 req/min
- `StrictPasswordOps`: 3 req/min

OPTIONS requests bypass rate limiting (CORS preflight).

## Security Headers

Applied globally in middleware: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.

## Shipping Cost Logic

In `OrdersController.CreateFromCart()`, the shipping cost is hardcoded:
- Free if order subtotal ≥ $50
- $3.50 otherwise

See comment `// CAMBIAR COSTO DE ENVÍO AQUÍ` at line 100 of `OrdersController.cs`.

## Webhook Signature Verification

`PlacetoPay:VerifyWebhookSignature: true` enables signature checks. The expected signature is `SHA-256(requestId + status.status + status.date + secretKey)`, or SHA-1 for legacy test environments.
