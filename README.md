# sheaf
Leader-first relational capture for churches: capture the people you meet, keep 'my people' in your phone, and follow up with AI drafts you send yourself. AI for Church Hackathon 2026.

## Development

Prerequisites: .NET SDK (see `global.json`), Docker, and the [Aspire CLI](https://aspire.dev).

```pwsh
aspire secret set Parameters:auth-signing-key "<at least 32 characters>"
aspire run          # API, SQL Server, migrations and the React frontend; API at http://localhost:5285
```

The React + Vite PWA lives in `Sheaf.Web` (requires Node and pnpm; Aspire runs `pnpm install` and `pnpm dev`, proxying `/api` to the backend).

Backend-only build/test: `dotnet build Sheaf.Backend.slnf`, then `dotnet test --project <test csproj>`.

### Database tenancy

Runtime `AddDatabase()` and `DesignTimeAppDbContextFactory` both use `Sheaf.Data`'s
`AddSheafDatabase()` to configure Azure SQL, the tenancy convention and write interceptors, and
Identity schema version 3. `ApplicationDbContext` refuses to initialize without `UseMultiTenancy()`.
SQLite tests keep their test provider and enable the same tenancy extension directly.

Church-owned entities implement only `Sheaf.Data.Tenancy.ITenanted`. The request-scoped
`ICurrentChurch` exposes nullable church/leader selection; `TenantContext` remains its mutable
implementation. This is distinct from `Tenancy.ITenantContext`, which requires a church ID:
`ApplicationDbContext` adapts request state through `ChurchIdOrThrow`. An options-only context
can build the model without a selected church, but queries against tenanted entities fail closed.
When authentication selection is wired, `LeaderId` denotes the church membership ID, not a global
Identity user ID.

`SaveChanges` protects tracked writes, including changes to owned entities, which
are attributed to their root `ITenanted` owner even when the owner itself is unchanged. `ExecuteUpdate`/`ExecuteUpdateAsync` also reject explicit
assignments to mapped `ITenanted.ChurchId`, including no-op assignments and queries matching no rows.
The query interceptor checks setter targets, not values or projected member names: ordinary payload
updates remain tenant-filtered. Direct mapped properties, `EF.Property`, runtime setter builders,
and mapped targets reached through `Select`, inner/left/right joins, set operations, and correlated
`SelectMany` are supported when EF can translate the update. Set-operation branches must resolve to
the same mapped property; projected scalar aliases do not change a column's ownership semantics.
A setter whose mapped target cannot be resolved fails closed with `InvalidOperationException`;
unsupported `ExecuteUpdate` source shapes such as `GroupJoin` (including
`GroupJoin`/`SelectMany`/`DefaultIfEmpty` left joins) fail closed.
`ExecuteUpdate`/`ExecuteDelete` (sync and async) are rejected before any mutation when the query
ignores `TenancyFilter`, whether through `IgnoreQueryFilters()` or a named
`IgnoreQueryFilters(["TenancyFilter"])`, and either the written entity type is `ITenanted` (including
tenanted rows reached through a navigation such as `SelectMany`) or the query has a tenanted entity
set root anywhere. Bulk writes to global entities with no tenanted root may still ignore filters.
Known limits: in such a global write, tenanted rows reached only through navigations (for example
`Where(hub => hub.Spokes.Any())`) are read unfiltered and can influence which global rows change; and
deleting a global principal can trigger database-level cascades into tenanted dependents of every
church, which EF query filters do not see.
Cross-church maintenance writes need a separately audited API.
This is an EF-layer ownership guard,
not database row-level security: trusted raw SQL, external writers, and deliberately bypassed query
filters on reads or tracked loads still require their own authorization boundaries.

This tenancy-foundation change adds only the minimal church-scoped `ChurchMembership` entity and
production migration needed to prove tenant filtering and write guards on the migrated
`ApplicationDbContext`. The broader #20 domain model and its migration remain follow-up work,
stacked on this tenancy foundation.

The existing production migration chain can be validated on the fixture's isolated SQL Server
catalog with `Sheaf.Core.Tests.SqlServerMigrationTests` (Docker/amd64 required).

Check model/snapshot parity through both configuration paths without creating a migration:

```pwsh
dotnet ef migrations has-pending-model-changes --project Sheaf.Data\Sheaf.Data.csproj --startup-project Sheaf\Sheaf.csproj
dotnet ef migrations has-pending-model-changes --project Sheaf.Data\Sheaf.Data.csproj --startup-project Sheaf.AppHost\Sheaf.AppHost.csproj
```

The backend check also needs `Auth:SigningKey`; supply local-only configuration for model checks.

The runtime options probes are in `Sheaf.Core.Tests.TenancyTests`; factory probes are in
`Sheaf.AppHost.Tests.DesignTimeAppDbContextFactoryTests`. These model-only tests do not connect
to a database or start Aspire.

The SQL Server tenancy tests use the migrated production `ApplicationDbContext` and its actual
migrations to verify membership query isolation and cross-church write rejection. A separate
test-only model remains for distinct convention/owned-JSON coverage; its migration test applies
only that fixture's schema and does not stand in for the production migration path. Both use
`UseAzureSql`, so the test-only owned-JSON migration test requires SQL Server 2025 or Azure SQL.

SQL Server bulk-compatibility cases cover tenant-filtered updates and reject ownership changes
through `Union` and correlated `SelectMany` sources. Additional provider and alias cases remain in
the SQLite tenancy test suite.

These container tests require Docker and an amd64-capable host (the SQL Server image is
amd64-only), including CI on `ubuntu-latest`. Run the migration and isolation cases with:

```pwsh
dotnet test --project Sheaf.Core.Tests\Sheaf.Core.Tests.csproj --no-restore --treenode-filter "/*/*/(SqlServerMigrationTests)|(SqlServerProductionTenancyTests)|(SqlServerTenancyMigrationTests)|(SqlServerTenancyTests)|(SqlServerTenancyWriteGuardTests)|(SqlServerBulkUpdateCompatibilityTests)/*"
```

For local Podman environments that need an explicit reachable container host, set
`$env:TESTCONTAINERS_HOST_OVERRIDE = "127.0.0.1"` in the test process only; no machine-level
configuration change is required.

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

## License

AGPL-3.0. See `LICENSE`.
