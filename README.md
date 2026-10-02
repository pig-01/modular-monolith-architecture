# Modular Monolith Architecture

> ⚠️ **This is a DEMO project.** It is intended to demonstrate architectural patterns and is **not** production-ready out of the box.

A .NET 10 demo project showcasing **Modular Monolith Architecture** with two advanced data access patterns:

1. **Database-per-Tenant** — each authenticated user's data is stored in their own isolated database, selected automatically via JWT claims.
2. **Multi-Source Aggregation** — a single user can register multiple heterogeneous databases (MSSQL, MySQL, PostgreSQL, Oracle) and query across all of them in parallel, with each result tagged by its origin.

---

## 最小啟動：Aspire + Podman Compose

已使用 `aspire init --language csharp --suppress-agent-init --non-interactive` 建立 AppHost。
`src/ModularMonolith.AppHost/AppHost.cs` 統一定義 **SQL Server 就緒 → seed 成功結束 → Web API**，
由 Aspire 產生 Compose，服務與相依關係只需在 AppHost 維護。

### 全容器部署

需要 `global.json` 指定的 .NET 10 SDK、Aspire CLI 13.6.0、Podman 5+ 與 Compose provider
（`podman compose version` 應能成功）。SQL Server 使用 `2025-latest` Developer 映像，
需要 x86-64 Linux 容器；Windows 使用 Podman machine。

在儲存庫根目錄執行：

```powershell
# machine 尚未啟動時先執行 podman machine start
$env:ASPIRE_CONTAINER_RUNTIME = 'podman'
# 可重跑；已存在時沿用原資料卷
podman volume create modular-monolith_sqlserver-data
aspire deploy -e local -o artifacts/compose --non-interactive
```

Aspire 會使用根目錄 Dockerfile 的 `seed` 與 `webapi` targets 建置映像、產生 Compose，
再透過 Podman 啟動。兩個應用程式容器都以非 root 使用者執行。

- Swagger：<http://localhost:8080/swagger>
- SQL Server：`localhost,14330`，帳號 `sa`。
- `artifacts/compose/docker-compose.yaml`：產生的服務設定，不需手動修改。
- `artifacts/compose/.env.local`：映像名稱、`SQLSERVER_PASSWORD` 與 `JWT_KEY`，包含機密且已由 Git 忽略。
- SQL 密碼與 JWT 金鑰未指定時自動產生；部署參數會存入 Aspire 的 `local` 部署狀態，後續部署沿用。
- 連接埠只綁定本機。SQL 使用 `modular-monolith_sqlserver-data` external volume，重建容器保留資料。

此設定固定使用一個 `local` 環境。Aspire 13.6 依 AppHost 路徑產生 Compose project 名稱，
容器名稱與 external 資料卷名稱則固定，避免路徑改變時無意切換資料卷。
已有資料卷時，必須沿用原 SQL 密碼；更換 Aspire 部署狀態或參數不會更改資料庫內的 SA 密碼。
從先前手寫 Compose 遷移時，需先將原 `.env` 的 `MSSQL_SA_PASSWORD`、`JWT_KEY` 分別作為
AppHost 的 `Parameters__sqlserver-password`、`Parameters__jwt-key` 程序環境變數傳入首次部署。
根目錄 `.env` 不會由 Aspire 自動載入；本機既有值已在遷移驗證時帶入。

僅產生 YAML 與空白參數檔供檢視（不建置映像、不啟動容器）：

```powershell
aspire publish -o artifacts/compose --non-interactive
```

部署後可直接使用產出的 Compose。先讀取 Aspire 實際使用的 project 名稱，避免操作到另一組容器：

```powershell
$project = podman inspect modular-monolith_sqlserver_1 --format '{{index .Config.Labels "com.docker.compose.project"}}'
$composeArgs = @('-p', $project, '-f', 'artifacts/compose/docker-compose.yaml', '--env-file', 'artifacts/compose/.env.local')
podman compose @composeArgs ps -a
podman compose @composeArgs logs seed
# 停止並移除容器，保留 external SQL 資料卷
podman compose @composeArgs down
# 在同一個 shell 中可使用原 project 名稱重新啟動
podman compose @composeArgs up -d
```

`seed` 正常完成應為 `Exited (0)`；初始化失敗時 API 不會啟動。
SQL external volume 由 Podman 獨立管理，停止與重新部署不會刪除它。

### 本機開發：.NET 程式 + Podman SQL Server

同一份 AppHost 也支援本機執行 .NET 程式：

```powershell
$env:ASPIRE_CONTAINER_RUNTIME = 'podman'
aspire start --isolated --non-interactive
aspire wait webapi --non-interactive
aspire describe --non-interactive
aspire stop --non-interactive
```

從 Aspire Dashboard 的 `webapi` HTTP 端點開啟 `/swagger`。
`--isolated` 使用隔離環境、連接埠與 user-secrets，方便與其他 AppHost 共存；
開發模式與 Compose 部署使用各自的資料卷與密碼。兩種模式均由 AppHost 注入 SQL 連線字串與 JWT 金鑰。
開發 Dashboard 提供資源狀態與 console logs；未加入 ServiceDefaults / OpenTelemetry，Compose 部署不另啟 Dashboard。

### 資料庫與驗證

只有一個 SQL Server 容器。`ModularMonolithDemo` 為共用資料庫，seed 套用四個模組的 migrations，
並在空表加入 10 筆 User、30 筆 Product；重跑 seed 不會重複新增。
`Tenant1DB`、`Tenant2DB` 保留租戶隔離，由 User 模組首次收到對應 JWT 時套用 migration。
Aspire 開發模式會先建立兩個租戶資料庫；Compose 則由 User 模組首次使用時建立。
租戶 Users 表初始為空，共用資料庫的示範 Users 不會複製到租戶資料庫。

```powershell
# 檢查容器狀態、Swagger、Product、Order，以及兩個租戶的 User / DataSource API
./scripts/Test-Deployment.ps1
# 驗證 seed 可重跑
podman compose @composeArgs run --rm seed
./scripts/Test-Deployment.ps1

dotnet test src/ModularMonolith.slnx
```

`/users/` 需要含 `tenant_id=tenant1` 或 `tenant2` 的有效 JWT；`/datasources/` 另外需要 GUID 格式的 `sub`。
部署檢查會從本機 API 容器讀取簽章金鑰產生測試 JWT，不輸出金鑰或 token；請在已部署的主機執行。

此 Windows / Podman 環境的一般 Aspire 開發啟動曾停在 `Starting`；隔離模式已完成端到端驗證。
方案 46 個測試通過。

2026-10-02：`aspire publish` 與 `aspire deploy -e local` 成功；部署流程 25/25 步驟通過，
沿用既有 SQL 資料卷，部署檢查與 seed 重跑均成功，產品仍為 30 筆。

參考：[Aspire Compose 部署](https://aspire.dev/deployment/docker-compose/)、
[SQL Server hosting 整合](https://aspire.dev/integrations/databases/sql-server/sql-server-host/)、
[Aspire 隔離啟動](https://aspire.dev/reference/cli/commands/aspire-start/)、
[SQL Server Linux 容器](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker?view=sql-server-ver17)。

---

## Architecture Overview

```
src/
├── apps/
│   ├── webapi/          # ASP.NET Core Minimal API host
│   └── seed/            # Database seeding utility
└── modules/
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
| Authentication | JWT Bearer (`tenant_id` + `sub` claims) |

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

Every request to a User endpoint is routed to the **calling user's own database**, identified by the `tenant_id` claim in the JWT token.

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
TenantUserDbContextFactory               builds UserDbContext + MigrateAsync (once per tenant)
     │
     ▼
UserDbContext → Tenant1 DB (SQL Server)
```

**Schema guarantee:** On the first request for each tenant, EF Core `MigrateAsync()` is called automatically, ensuring every tenant database has an identical, up-to-date schema.

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
| `sub` / `user_id` | Identifies the authenticated user (used by DataSource module) |

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

| Module | Method | Endpoint | Auth Required |
|--------|--------|----------|:---:|
| User | `POST` | `/users` | ✅ (tenant_id) |
| User | `GET` | `/users` | ✅ (tenant_id) |
| User | `GET` | `/users/{id}` | ✅ (tenant_id) |
| Order | `POST` | `/orders` | — |
| Order | `GET` | `/orders` | — |
| Order | `GET` | `/orders/{id}` | — |
| Product | `POST` | `/products` | — |
| Product | `GET` | `/products` | — |
| Product | `GET` | `/products/{id}` | — |
| DataSource | `POST` | `/datasources` | ✅ |
| DataSource | `GET` | `/datasources` | ✅ |
| DataSource | `DELETE` | `/datasources/{id}` | ✅ |
| DataSource | `GET` | `/datasources/query/users` | ✅ |

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

`User.IntegrationTest` 目前未列入方案；其中 `TenantUserDbContextFactoryTests` 仍使用舊的三參數建構函式，獨立建置會出現兩處 CS1729。這是升級前即存在的測試問題。若要整理此專案的格式，另執行：

```bash
dotnet format src/modules/User/tests/User.IntegrationTest/User.IntegrationTest.csproj --no-restore
dotnet format src/modules/User/tests/User.IntegrationTest/User.IntegrationTest.csproj --no-restore --verify-no-changes --exclude-diagnostics IDE1006
```

升級相容性依據：[Swashbuckle v10 遷移指南](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/migrating-to-v10.md)、[MySQL EF Core 套件](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9)、[EF Core 10 變更說明](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)。資料庫驅動與 migration 模型以不連線的測試驗證；實際資料庫連線仍需在對應環境驗收。

本次升級驗證：方案建置成功、45/45 測試通過；Swagger JSON、JWT Bearer 文件設定、Swagger UI 與 DataSource 未登入回傳 401 均通過。NuGet 查核未發現落後的直接套件或已知弱點（含間接相依套件）。

---

## Getting Started

### Prerequisites

- .NET 10.0.401 SDK 或同 feature band 的較新修補版
- SQL Server instance (for master DB and/or tenant DBs)

### 1. Configure Connection Strings

Edit `src/apps/webapi/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=localhost;Initial Catalog=ModularMonolith;Integrated Security=SSPI;TrustServerCertificate=true"
},
"Jwt": {
  "Issuer": "ModularMonolith",
  "Audience": "ModularMonolith",
  "Key": "YOUR_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG"
},
"Tenants": {
  "tenant1": "Data Source=localhost;Initial Catalog=Tenant1DB;Integrated Security=SSPI;TrustServerCertificate=true"
}
```

### 2. Run Migrations

```bash
cd src

# Initial migrations are already checked in; apply them to the configured database.
# Compose and Aspire run the seed utility automatically, so these commands are only
# needed when running against your own SQL Server without either orchestrator.
dotnet ef database update -p modules/User/Infrastructure        -s apps/webapi
dotnet ef database update -p modules/Order/Infrastructure       -s apps/webapi
dotnet ef database update -p modules/Product/Infrastructure     -s apps/webapi
dotnet ef database update -p modules/DataSource/Infrastructure  -s apps/webapi
```

### 3. Run the API

```bash
cd src/apps/webapi
dotnet run
```

Swagger UI is available at: `https://localhost:<port>/swagger`

### 4. Seed Sample Data

```bash
cd src/apps/seed
dotnet run
```

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
- The **JWT signing key** in `appsettings.json` is a placeholder. Store it in environment variables or a secrets manager.
- The `InMemoryTenantConnectionStringResolver` reads tenant config from `appsettings.json`. Replace with a **database-backed resolver** for dynamic tenant provisioning.
- **External data source credentials** used by the multi-source query should have **read-only** database permissions.
- **User endpoints** currently lack `RequireAuthorization()`, despite the intended JWT requirement described above. An unauthenticated `/users` request reaches tenant resolution and returns HTTP 500. This pre-existing behavior is separate from the package upgrade; add authorization and HTTP regression coverage before production use.
