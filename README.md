# Modular Monolith Architecture

> ⚠️ **This is a DEMO project.** It is intended to demonstrate architectural patterns and is **not** production-ready out of the box.

A .NET 10 demo project showcasing **Modular Monolith Architecture** with two advanced data access patterns:

1. **Database-per-Tenant** — tenant data lives in separate databases, selected from the authenticated session and validated membership.
2. **Multi-Source Aggregation** — a single user can register multiple heterogeneous databases (MSSQL, MySQL, PostgreSQL, Oracle) and query across all of them in parallel, with each result tagged by its origin.

---

## 啟動：Docker Compose + HTTPS 登入網站

根目錄 `compose.yaml` 直接啟動 **SQL Server → Auth／共用／全部 tenant migrations 與 seed → 單一 API → HTTPS Nginx 前端**，並包含本機 Mailpit 信箱。不需要 Aspire CLI；既有 AppHost 仍可用於 .NET 本機開發與另一種 Compose 發佈流程。

需要啟動中的 Docker Desktop／Engine、Docker Compose、PowerShell 7，以及 .NET 10 SDK（建立本機憑證）。SQL Server 使用 x86-64 Linux 容器。

```powershell
./scripts/Initialize-LocalDeployment.ps1
docker compose --env-file artifacts/compose/.env.local up --build -d
```

- 網站：<https://localhost:8443>；支援註冊、email 確認、登入、忘記／重設密碼及受保護的帳號頁。
- Mailpit：<http://localhost:8025>；註冊後從此收取確認信。
- 一般帳號預設加入 `tenant1`；其他 membership 由 seed 指令授予，切換租戶同步同瀏覽器各分頁。
- 管理員：`admin@example.test`；初始密碼於本機隨機產生，保存在忽略的 `artifacts/compose/.env.local`。重跑 seed 不重設既有密碼。
- API 只透過同源 `/api` proxy 存取；SQL 管理埠只綁定 `localhost,14330`。
- SQL 與 DP key ring 使用固定 external volumes；憑證與設定只於執行時掛載，沒有打包進 image。

```powershell
$credential = ./scripts/Get-LocalAdministratorCredential.ps1
./scripts/Test-Deployment.ps1 -Credential $credential
docker compose --env-file artifacts/compose/.env.local logs seed
# 重跑 migrations/seed，保留既有資料與密碼：
docker compose --env-file artifacts/compose/.env.local run --rm seed
# 對已註冊帳號授予 allowlist 內的 tenant：
docker compose --env-file artifacts/compose/.env.local run --rm seed grant-membership person@example.test tenant2
```

部署檢查使用 CSRF + 真實登入與 cookies，不自行簽發 JWT。初始化會沿用已有 `.env.local`；如已有 SQL volume 卻缺少參數檔，必須提供原 `SQLSERVER_PASSWORD`，不會產生不同密碼覆蓋它。Docker 與 Podman 的資料卷分屬不同引擎；切換引擎不會自動搬移資料。

停止時使用 `docker compose --env-file artifacts/compose/.env.local down`，external volumes 仍保留。正式環境 SMTP、TLS、金鑰輪換、備份及 `deploy/compose.production.yaml` 用法見 [Auth deployment runbook](docs/runbooks/auth-deployment.md)。

### 本機前後端開發

保留後面的 Vite 指令即可開發 React；依 [frontend README](src/apps/web/README.md) 使用同一組 HTTPS 憑證與 API proxy。既有 Aspire 13.6 AppHost 可用 `aspire start --isolated --non-interactive` 啟動 .NET 開發服務；Vite HMR 時將 `Deployment__FrontendUrl` 設為 Vite 的 HTTPS origin。完整容器部署請優先使用上面的 Docker Compose 指令。

---

## Architecture Overview

```
src/
├── apps/
│   ├── webapi/          # ASP.NET Core Minimal API host
│   ├── web/             # React + TypeScript frontend (Vite)
│   └── seed/            # Database seeding utility
└── modules/
    ├── Auth/            # Central Identity, memberships, sessions and login endpoints
    ├── User/            # User module (tenant-aware CRUD)
    ├── Order/           # Order module
    ├── Product/         # Product module
    └── DataSource/      # Multi-source query module (NEW)
```

Each module follows **Clean Architecture** layering:

```
Module/
├── Domain/          # Entities, Domain Events (no dependencies)
├── Infrastructure/  # EF Core DbContext, Migrations, Factories
├── Application/     # CQRS Handlers, Validators, Endpoints, DI
└── IntegrationEvent/# Cross-module integration event contracts
```

### Key Patterns

| Pattern | Implementation |
|---------|---------------|
| CQRS | Mediator — Commands and Queries separated |
| Validation | FluentValidation via Mediator pipeline |
| Transaction | `TransactionScope` via Mediator pipeline |
| Intra-module events | Mediator `INotification` (domain events) |
| Inter-module events | Mediator `INotification` (integration events) |
| Authentication | JWT in HttpOnly cookies; Bearer validation; database-backed sessions |

---

## Mediator 設定

本專案採用 [martinothamar/Mediator](https://github.com/martinothamar/Mediator)，參考官方 [Clean Architecture sample](https://github.com/martinothamar/Mediator/tree/v3.0.2/samples/apps/ASPNET_Core_CleanArchitecture)。

- 模組的 Application、Domain 與 IntegrationEvent 使用 `Mediator.Abstractions`；Web API 入口使用 `Mediator.SourceGenerator`，由 `AddApplicationMediator()` 統一註冊所有模組的處理器。
- Mediator 與處理器使用 `Scoped`，配合 EF Core 與租戶服務的生命週期。新增查詢或命令處理器需為 `public`，讓入口專案產生的程式碼能存取。
- 處理器回傳 `ValueTask<T>`，通知處理器回傳 `ValueTask`。各模組保留驗證與交易管線，透過 `next(request, cancellationToken)` 傳遞請求與取消權杖。
- 各模組的獨立整合測試專案也使用 SourceGenerator；整體整合測試直接使用 Web API 的註冊與產生程式碼。

```bash
dotnet build src/ModularMonolith.slnx
dotnet test src/ModularMonolith.slnx
```

---

## Feature 1 — Database-per-Tenant

Every request to a User endpoint is routed to the **active tenant's database**, after validating the JWT, server-side session, and current membership.

```
POST /users  (JWT: tenant_id=tenant1)
     │
     ▼
JwtTenantProvider          reads "tenant_id" claim
     │
     ▼
InMemoryTenantConnectionStringResolver   appsettings.json → Tenants:tenant1
     │
     ▼
TenantUserDbContextFactory               builds UserDbContext for the validated session tenant
     │
     ▼
UserDbContext → Tenant1 DB (SQL Server)
```

**Schema guarantee:** Seed applies migrations to every configured tenant before API startup. HTTP requests never migrate databases. Only a tenant present in the authenticated session and current membership is accepted.

### Configuration

```json
// appsettings.json
"Jwt": {
  "Issuer": "ModularMonolith",
  "Audience": "ModularMonolith",
  "Key": "<replace-with-32+-char-secret>"
},
"Tenants": {
  "tenant1": "Data Source=...;Initial Catalog=Tenant1DB;...",
  "tenant2": "Data Source=...;Initial Catalog=Tenant2DB;..."
}
```

### JWT Token Claims Required

| Claim | Description |
|-------|-------------|
| `tenant_id` | Routes User module requests to the correct database |
| `sub` | Central Identity account ID (GUID); used by DataSource ownership filters |
| `sid` | Server-side session checked on every authenticated request |

---

## Feature 2 — Multi-Source Aggregation

Users can register multiple **external data sources** of different database types and query them all at once. Results are merged and annotated with the originating source.

```
GET /datasources/query/users  (JWT: sub=<userId>)
     │
     ▼
Load user's registered DataSources from master DB
     │
     ├── MSSQL Source  ──┐
     ├── MySQL Source  ──┤  Task.WhenAll (parallel)
     └── PG Source     ──┘
                         │
                         ▼
             SourcedResult<UserData>[]
             [{ data, sourceName, provider }, ...]
```

**Schema contract:** All registered external databases must contain a `Users` table with columns `Id (uniqueidentifier)`, `Name (nvarchar(200))`, `Email (nvarchar(256))`. The `ExternalUserDbContext` enforces this contract via EF Core Fluent API regardless of provider.

### Supported Providers

| Provider | EF Core Package |
|----------|----------------|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` |
| MySQL 8.0+ | `MySql.EntityFrameworkCore` |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Oracle | `Oracle.EntityFrameworkCore` |

MySQL 使用 Oracle 官方驅動；本專案不以 MariaDB 為支援目標。既有 Pomelo／MySqlConnector 專用的連線字串選項，需依 [Connector/NET 連線選項](https://dev.mysql.com/doc/connector-net/en/connector-net-8-0-connection-options.html) 調整。

### Data Source API

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/datasources` | Register a new data source |
| `GET` | `/datasources` | List your registered data sources |
| `DELETE` | `/datasources/{id}` | Remove a data source |
| `GET` | `/datasources/query/users` | **Query Users across all sources** |

**Request body for `POST /datasources`:**
```json
{
  "name": "My MySQL DB",
  "provider": 1,
  "connectionString": "Server=...;Database=...;User=...;Password=..."
}
```

Provider values: `0` = MSSQL, `1` = MySQL, `2` = PostgreSQL, `3` = Oracle

---

## All API Endpoints

Browser URLs prepend `/api`; Nginx removes that prefix before API routing. Auth endpoints are `/auth/csrf` and `/auth/me` (GET), plus `/auth/register`, `/auth/confirm-email`, `/auth/resend-confirmation`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/login`, `/auth/refresh`, `/auth/logout`, and `/auth/switch-tenant` (POST). Cookie-changing requests require the `X-CSRF-TOKEN` header from `/auth/csrf`. Access/refresh tokens are returned only through HttpOnly cookies.

| Module | Method | Endpoint | Auth Required |
|--------|--------|----------|:---:|
| User | `POST` | `/users` | Platform administrator |
| User | `GET` | `/users` | Platform administrator |
| User | `GET` | `/users/{id}` | Platform administrator |
| Order | `POST` | `/orders` | Platform administrator |
| Order | `GET` | `/orders` | Platform administrator |
| Order | `GET` | `/orders/{id}` | Platform administrator |
| Product | `POST` | `/products` | Platform administrator |
| Product | `GET` | `/products` | Platform administrator |
| Product | `GET` | `/products/{id}` | Platform administrator |
| DataSource | `POST` | `/datasources` | Platform administrator |
| DataSource | `GET` | `/datasources` | Platform administrator |
| DataSource | `DELETE` | `/datasources/{id}` | Platform administrator |
| DataSource | `GET` | `/datasources/query/users` | Platform administrator |

---

## Tech Stack

| Technology | Version |
|------------|---------|
| .NET SDK | 10.0.401（`global.json`，允許同 feature band 的修補版） |
| ASP.NET Core Minimal API / JWT Bearer | 10.0.12 |
| EF Core | 10.0.12 |
| Mediator | 3.0.2 |
| Mapperly | 4.3.1 |
| FluentValidation | 12.1.1 |
| Serilog.AspNetCore / Settings.Configuration / Console | 10.0.0 / 10.0.1 / 6.1.1 |
| Swashbuckle (Swagger) | 10.2.3 |
| MySQL / PostgreSQL / Oracle EF providers | 10.0.9 / 10.0.3 / 10.23.26301 |
| Microsoft.NET.Test.Sdk | 18.10.1 |
| xUnit / Visual Studio runner | 2.9.3 / 4.0.0 |
| coverlet.collector | 10.1.0 |
| CSharpier（既有選用工具） | 1.3.0 |

套件版本於 2026-10-02 查核 NuGet 最新穩定版，集中管理於 `src/Directory.Packages.props`。Mapperly、Mediator 與 xUnit 已是各自套件的最新穩定版。未使用的 BenchmarkDotNet 與 Microsoft.AspNetCore.OpenApi 版本宣告已移除。

全專案格式整理使用 SDK 內建的 `dotnet format`，遵循既有 `.editorconfig`：

```bash
dotnet format src/ModularMonolith.slnx --no-restore
dotnet format src/ModularMonolith.slnx --no-restore --verify-no-changes --exclude-diagnostics IDE1006
```

`dotnet format` 不支援 IDE1006 命名規則的批次修正，因此保留既有的私有欄位命名提示（方案內 35 處），驗證指令僅排除此項；未排除時會回傳 exit code 2。其餘格式與可自動修正的樣式均納入檢查。

升級相容性依據：[Swashbuckle v10 遷移指南](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/migrating-to-v10.md)、[MySQL EF Core 套件](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9)、[EF Core 10 變更說明](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)。資料庫驅動與 migration 模型以不連線的測試驗證；實際資料庫連線仍需在對應環境驗收。

本次升級驗證：方案建置成功、45/45 測試通過；Swagger JSON、JWT Bearer 文件設定、Swagger UI 與 DataSource 未登入回傳 401 均通過。NuGet 查核未發現落後的直接套件或已知弱點（含間接相依套件）。

---

## Getting Started

### Prerequisites

- .NET 10.0.401 SDK 或同 feature band 的較新修補版
- SQL Server instance (for master DB and/or tenant DBs)
- Node.js 24 or later and npm (for the React frontend)

### Backend configuration and startup

Use the Docker Compose procedure above for the complete runnable application. For an independently hosted API/seed, provide all settings from the [deployment runbook](docs/runbooks/auth-deployment.md): shared/Auth/tenant SQL connections, JWT signing key and key ID, public HTTPS frontend origin, tenant allowlist, DP certificate/key directory, and SMTP. Seed also needs the administrator credentials.

Run seed before API startup; it owns all migrations:

```bash
dotnet run --project src/apps/seed
dotnet run --project src/apps/webapi
```

Swagger is available only in the Development environment. Browser calls use the frontend's same-origin `/api` proxy.
### 5. Run the React Frontend

From the repository root, in a separate terminal:

```bash
cd src/apps/web
npm ci
npm run dev
```

Open the HTTPS URL printed by Vite after configuring the shared local certificates and API proxy.
The auth pages use the backend through `/api`. Run `npm run build` to type-check and create a
production build, or `npm run lint` to check the code.

See [the frontend README](src/apps/web/README.md) for more details.

---

## Project Structure Detail

```
src/modules/
├── User/
│   ├── Domain/Entities/User.cs
│   ├── Infrastructure/
│   │   ├── UserDbContext.cs
│   │   └── MultiTenant/
│   │       ├── ITenantProvider.cs
│   │       ├── ITenantConnectionStringResolver.cs
│   │       ├── InMemoryTenantConnectionStringResolver.cs
│   │       └── TenantUserDbContextFactory.cs
│   └── Application/
│       ├── MultiTenant/JwtTenantProvider.cs
│       ├── Commands/CreateUserCommand(Handler).cs
│       ├── Queries/UserQueries.cs
│       └── Endpoints/UserEndpoints.cs
│
└── DataSource/
    ├── Domain/Entities/DataSource.cs
    ├── Infrastructure/
    │   ├── DataSourceDbContext.cs
    │   └── MultiDb/
    │       ├── IMultiDbContextFactory.cs
    │       ├── MultiDbContextFactory.cs      ← provider switch (MSSQL/MySQL/PG/Oracle)
    │       ├── ExternalUser.cs               ← shared schema contract entity
    │       └── ExternalUserDbContext.cs
    └── Application/
        ├── Services/MultiSourceQueryService.cs  ← Task.WhenAll parallel query
        ├── Abstractions/SourcedResult.cs
        └── Endpoints/DataSourceEndpoints.cs
```

---

## Security Notes

> These are known limitations of the demo — address them before any production use.

- **Connection strings** for registered data sources are stored as plain text. Use [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction) or Azure Key Vault to encrypt them.
- The **JWT signing key** is required from deployment secrets; no signing key is checked into `appsettings.json`.
- The `InMemoryTenantConnectionStringResolver` reads tenant config from `appsettings.json`. Replace with a **database-backed resolver** for dynamic tenant provisioning.
- **External data source credentials** used by the multi-source query should have **read-only** database permissions.
- Existing business endpoints require the platform administrator policy; public registrations only access account capabilities. Identity accounts are separate from tenant business User profiles.
