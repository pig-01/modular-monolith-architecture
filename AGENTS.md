# Repository Guidelines

## Project Structure & Module Organization

`src/apps/webapi` hosts the ASP.NET Core API; `src/apps/seed` seeds databases. `src/apps/web` contains React/TypeScript, with assets in `src/assets` and `public`. Backend modules live under `src/modules/{User,Order,Product,DataSource}`. Keep entities in `Domain`, persistence in `Infrastructure`, and CQRS handlers, validators, and endpoints in `Application`. Existing `IntegrationEvent` projects hold cross-module contracts. Tests belong under each module's `tests` directory.

## Build, Test, and Development Commands

Use .NET 10 and Node.js 24+. Run backend commands from the repository root:

- `dotnet build src/ModularMonolith.slnx` restores dependencies and builds the solution.
- `dotnet test src/ModularMonolith.slnx` runs solution-included tests.
- `dotnet run --project src/apps/webapi` starts the API.
- `dotnet run --project src/apps/seed` populates the configured databases.

In `src/apps/web`, run `npm ci` to install dependencies, `npm run dev` for Vite, `npm run build` for TypeScript checks and production output, and `npm run lint` for ESLint.

## Coding Style & Naming Conventions

Follow `.editorconfig`: four-space C# indentation, CRLF, and opening braces on new lines; two spaces elsewhere. The frontend overrides encoding to UTF-8 without BOM. Use PascalCase for C# types/methods and descriptive suffixes such as `CommandHandler`, `CommandValidator`, and `DbContext`. Manage NuGet versions in `src/Directory.Packages.props`.

## Testing Guidelines

Use xUnit `[Fact]`, `*Tests` classes, and behavioral names such as `Price_must_be_positive`. Cover validation in unit tests and persistence/handler behavior in integration tests; existing integration tests use EF Core InMemory. The solution omits User tests, so also run:

```sh
dotnet test src/modules/User/tests/User.UnitTest
dotnet test src/modules/User/tests/User.IntegrationTest
```

Append `--collect:"XPlat Code Coverage"` for Coverlet reports; no coverage threshold is configured. The frontend currently has no test runner.

## Commit & Pull Request Guidelines

History mixes imperative subjects with `feat:` and `feat(DataSource):` prefixes. Use a concise imperative subject, optionally scoped by module. In PRs, describe behavior changes, link relevant issues, report validation results, and explain configuration or migration impacts. Include screenshots for UI changes.

## Configuration & Agent Guidance

Keep JWT keys and database credentials in environment variables or a secrets manager. Consult `README.md` for tenant configuration and migrations.

For code discovery, prefer MCP `search_graph`, `trace_path`, `get_code_snippet`, and `query_graph`; index unindexed repositories first. Use text search for configuration, literals, or insufficient graph results.
