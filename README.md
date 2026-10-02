# Modular Monolith Architecture

> ⚠️ **This is a DEMO project.** It is intended to demonstrate architectural patterns and is **not** production-ready out of the box.

A .NET 10 demo project showcasing **Modular Monolith Architecture** with two advanced data access patterns:

1. **Database-per-Tenant** — each authenticated user's data is stored in their own isolated database, selected automatically via JWT claims.
2. **Multi-Source Aggregation** — a single user can register multiple heterogeneous databases (MSSQL, MySQL, PostgreSQL, Oracle) and query across all of them in parallel, with each result tagged by its origin.

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
| MySQL | `Pomelo.EntityFrameworkCore.MySql` |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Oracle | `Oracle.EntityFrameworkCore` |

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
| .NET | 10 |
| ASP.NET Core Minimal API | 10 |
| EF Core | 8.0.8 |
| Mediator | 3.0.2 |
| FluentValidation | 11.9.0 |
| Serilog | 8.0.1 |
| Swashbuckle (Swagger) | 6.5.0 |

---

## Getting Started

### Prerequisites

- .NET 10 SDK
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

# User module (tenant schema — also auto-applied at runtime)
dotnet ef migrations add Initial_User -p modules/User/Infrastructure -s apps/webapi

# Order & Product modules
dotnet ef migrations add Initial_Order   -p modules/Order/Infrastructure   -s apps/webapi
dotnet ef migrations add Initial_Product -p modules/Product/Infrastructure  -s apps/webapi

# DataSource module (master registry)
dotnet ef migrations add Initial_DataSource -p modules/DataSource/Infrastructure -s apps/webapi

# Apply all migrations
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
