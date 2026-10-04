# Authentication deployment and operations

The root `compose.yaml` runs one API behind the HTTPS frontend proxy directly with Docker Compose. Aspire CLI is not required. The existing Aspire 13.6 AppHost remains available for .NET development. Keep `modular-monolith_sqlserver-data` and `modular-monolith_dataprotection-keys` as external volumes and preserve existing SQL passwords when replacing containers. Docker and Podman use different engine storage; changing engines does not migrate data.

## Local setup

Requirements: Docker with Compose, PowerShell 7, the SDK in `global.json` for local certificates, and x86-64 Linux containers for SQL Server. Node.js 24+ is needed for host-side frontend development; container builds include Node. Start Docker separately.

```powershell
./scripts/Initialize-LocalDeployment.ps1
docker compose --env-file artifacts/compose/.env.local up --build -d
```

Initialization calls `Setup-LocalCertificates.ps1` when needed, creates local secrets and the two external volumes, and preserves existing values. The certificate script trusts/exports the development HTTPS certificate, creates a separate DP encryption certificate, and copies initial lifetime settings. `-SkipTrust` supports automated tests that explicitly allow the local certificate; normal browser use should trust the certificate. `artifacts/` is ignored by Git and the Docker build context. The DP password is retained in local user-secrets and copied into the ignored Compose parameter file without printing it. Certificates are mounted at runtime. Do not use the local certificate script for production certificates.

Open <https://localhost:8443>; local email is available in Mailpit at <http://localhost:8025>. Registration requires email confirmation and initially grants only `tenant1`. The seeded administrator is `admin@example.test` with both configured memberships and a randomly generated initial password. Re-running seed never resets that password.

```powershell
# Retrieve the local bootstrap credential without displaying its password.
$credential = ./scripts/Get-LocalAdministratorCredential.ps1
./scripts/Test-Deployment.ps1 -Credential $credential
# For manual browser login, explicitly reveal only in your private terminal:
# $credential.GetNetworkCredential().Password
```

The helper reads the local seed container; it never reads JWT keys. After a password reset, supply the current credential instead. Do not paste container environment output into issues. SQL is loopback-only on port 14330 locally; the API has no published Compose port. Nginx serves SPA routes and strips `/api/` when forwarding to ASP.NET. Cookies remain Secure and HttpOnly, including on localhost.

To validate Compose without building images or starting services:

```powershell
docker compose --env-file artifacts/compose/.env.local config --quiet
```

The generated `.env.local` contains secrets; back it up securely. New parameter values do not change a password inside an existing SQL volume. When moving from a previous deployment, preserve `.env.local` or supply its existing `SQLSERVER_PASSWORD` and `JWT_KEY` environment variables before initialization. Initialization refuses to generate an unrelated password for an already existing SQL volume.

## Migrations and membership administration

Startup order is SQL healthy → seed completed successfully → API → frontend. Seed migrates Auth, shared business storage, and every configured tenant database before API startup. HTTP requests never apply migrations. The existing 10 demo Users and 30 Products remain idempotent; public Identity registration never creates a tenant business User profile.

```powershell
$composeArgs = @('-f', 'compose.yaml', '--env-file', 'artifacts/compose/.env.local')
docker compose @composeArgs ps -a
docker compose @composeArgs logs seed
docker compose @composeArgs run --rm seed
docker compose @composeArgs run --rm seed grant-membership person@example.test tenant2
```

Membership grants require an existing registered account and a tenant in `Auth:Tenants` with an explicit `Tenants` connection string. They do not create accounts or business profiles. Login still requires email confirmation. Ordinary accounts use the account UI; all existing business APIs require the platform administrator policy and preserve ownership filters.

If migration fails, fix the cause and rerun seed. Do not bypass its completion dependency. Back up SQL before schema updates. Ordinary Compose `down`/`up` keeps the external volumes; do not use volume deletion as a recovery procedure.

## Production configuration

Use Docker Compose 2.24.4+ and the production override. Prepare an operator-owned secret environment file outside source control with `SQLSERVER_PASSWORD`, `JWT_KEY`, `JWT_KEY_ID`, `DP_CERTIFICATE_PASSWORD`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `FRONTEND_URL` (public HTTPS origin), and `PUBLIC_HOSTNAME` (host without a scheme/port). Set `CERTIFICATE_DIRECTORY` and `SETTINGS_DIRECTORY` to absolute paths. Certificate files are `https/tls.pem`, `https/tls.key`, and `dataprotection/dataprotection.pfx`. Use a trusted TLS certificate and separately managed DP certificate. The API/seed mount only the DP directory; Nginx mounts only TLS files.

Production also requires `SMTP_HOST`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM_ADDRESS`, and `MSSQL_PID` (the appropriate SQL Server edition/license). `SMTP_PORT` defaults to 587; `SMTP_FROM_NAME` controls the display name, and STARTTLS is enabled. Mailpit is excluded by profile. Use SMTP credentials limited to the intended sender. Keep the fixed external volumes and existing database password.

Set `DP_APPLICATION_NAME` to a fixed, environment-specific value such as `ModularMonolith.Auth.Production`, and `DP_KEY_VOLUME` to a dedicated external volume such as `modular-monolith-production_dataprotection-keys`. Create that volume before the first deployment. Use a separate production host/storage for SQL and certificates; never mount a development key ring or certificate in production.

```powershell
docker compose --env-file /secure/production.env -f compose.yaml -f deploy/compose.production.yaml config --quiet
docker compose --env-file /secure/production.env -f compose.yaml -f deploy/compose.production.yaml up --build -d
```

The override removes SQL host ports, disables the local Mailpit dependency, and publishes frontend HTTPS on port 443 (configurable with `HTTPS_PORT`/`HTTPS_BIND_ADDRESS`). It uses `!reset`/`!override` and therefore requires a compatible Compose version. The local Compose file uses ordinary Compose constructs and remains usable with Podman Compose.

On Linux grant only the operator and required container identity read/traverse access to certificates. .NET uses UID 1654; unprivileged Nginx uses UID 101. Read-only bind mounts still need host permissions. Both .NET images initialize `/app/dpkeys` for UID 1654; check ownership on a pre-existing key volume.

Nginx adds HSTS, nosniff, no-referrer, Permissions-Policy, and CSP with self-only scripts/styles/connections, no objects/base URI/framing, and self-only forms. Only the HTTPS frontend is published in production. API forwarded headers trust one hop from the Compose CIDR, default `172.28.90.0/24`. If this conflicts with a host network, set `COMPOSE_NETWORK_CIDR`; Compose changes the network and trust configuration together. Keep the API private and never enable wildcard credentialed CORS.

## Lifetime reloads and JWT signing-key rotation

`Auth__SettingsFile` points to the mounted `authsettings.json`; start from `deploy/authsettings.example.json`. `IOptionsMonitor` reads defaults of Access 15 minutes, Session 7 absolute days, and Refresh replay 30 seconds. Edit/atomically replace the host file within the mounted directory. Environment variables override JSON and require container recreation; avoid lifetime environment overrides when live reload is intended.

New issuance uses current Access settings; new sessions capture Session duration. Existing sessions retain their absolute deadline. Rotation/retries never extend it or the cached token pair. Invalid initial settings fail validation; invalid reloads log an error and retain the last valid policy. Container polling watches mounted JSON, so allow a few seconds for a change. Auth database outages fail closed and are service failures, not invalid passwords.

JWT signing keys are separate from DP keys. For manual JWT rotation:

1. Generate a random secret of at least 32 bytes in the secret store and choose a unique `Jwt:KeyId`.
2. Put the former `{ Id, Key }` in `Jwt:PreviousKeys` in protected mounted JSON, supply the new `JWT_KEY` and `JWT_KEY_ID`, and recreate the API.
3. Verify new login/refresh and a still-valid token signed by the previous key.
4. Remove the previous key only after all tokens it signed expire plus validation clock skew. A compromised key instead requires immediate removal and session revocation.

## Data Protection, backups, and email recovery

DP automatically rotates payload-protection keys every 90 days and persists the encrypted ring. Local Compose defaults to application name `ModularMonolith.Auth.Local` and volume `modular-monolith_dataprotection-keys`; production requires its own `DP_APPLICATION_NAME` and `DP_KEY_VOLUME`. AppHost uses `.Local`/`.Production` names (overridable with `Deployment:DataProtectionApplicationName`) and a separate production DP volume. Keep expired DP keys: they still decrypt antiforgery, Identity email/reset tokens, and refresh retry payloads. Back up SQL Auth data, the DP volume, certificates/private keys and passwords, and deployment parameters together.

The application name is part of the protection boundary. When upgrading an existing installation, retain its previous name through configuration until planning a deliberate cutover; changing it invalidates existing CSRF and email/reset tokens and refresh retry payloads even when the same key ring is retained. Newly created environments must use distinct names and storage.

DP rotation does not rotate its wrapping certificate or JWT signing keys. For certificate rollover, retain the former PFX under another filename in the mounted DP directory, configure `DataProtection:PreviousCertificates` as `[{"Path":"/app/certificates/dataprotection-previous.pfx","Password":"<previous secret>"}]` in protected mounted settings, then replace the current PFX/password and recreate API/seed. Verify older protected payloads decrypt through `UnprotectKeysWithAnyCertificate`. Keep old certificates while any retained ring keys depend on them; do not delete expired ring keys. Never overwrite/delete the only working certificate or ring to fix an error. A database backup without compatible keys can invalidate links and sessions.

SMTP sends directly. Delivery failure leaves the unconfirmed account intact; fix SMTP and use resend confirmation. Forgot-password can be retried. Generic responses do not prove that an account exists or mail was delivered. Monitor SMTP failures, refresh rejections, and rate limits without recording passwords, JWTs, refresh tokens, or email tokens. No background outbox is deployed.

## Verification

```powershell
dotnet build src/ModularMonolith.slnx
# Supply AUTH_TEST_SQL_CONNECTION from your local secret store before this command.
dotnet test src/ModularMonolith.slnx
npm --prefix src/apps/web run build
npm --prefix src/apps/web run lint
npm --prefix src/apps/web run test -- --run
./scripts/Test-Deployment.ps1

$credential = ./scripts/Get-LocalAdministratorCredential.ps1
$env:E2E_ADMIN_EMAIL = $credential.UserName
$env:E2E_ADMIN_PASSWORD = $credential.GetNetworkCredential().Password
try { npm --prefix src/apps/web run test:e2e }
finally { $env:E2E_ADMIN_PASSWORD = $null; $env:E2E_ADMIN_EMAIL = $null }
```

The smoke check uses CSRF bootstrap, actual administrator login, cookies, protected APIs, tenant switches, missing-CSRF rejection, and logout. It never mints JWTs. Browser tests cover public registration/Mailpit confirmation, reset, concurrent refresh, multi-tab tenant/logout behavior, and CSP; HTTP tests also cover ordinary-user 403. Install the Playwright Chromium browser once with `npx playwright install chromium` from `src/apps/web`.

Set `AUTH_TEST_SQL_CONNECTION` to an isolated SQL Server connection with permission to create/drop databases. Each SQL fixture creates its own unique test database and removes it afterward. Without that variable, the relational Auth and HTTP flow tests are explicitly skipped; a default `dotnet test` run alone does not verify refresh concurrency. Both User test projects are now included in the solution. EF InMemory cannot verify SQL concurrency. Restart API and rerun checks to verify persistent keys.

References: [Aspire Compose](https://aspire.dev/deployment/docker-compose/), [DP configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [DP key management](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management), [unprivileged Nginx](https://github.com/nginx/docker-nginx-unprivileged).
