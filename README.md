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
`AddSheafDatabase()` to configure Azure SQL, the tenancy convention/save interceptor, and
Identity schema version 3. `ApplicationDbContext` refuses to initialize without `UseMultiTenancy()`.
SQLite tests keep their test provider and enable the same tenancy extension directly.

Church-owned entities implement only `Sheaf.Data.Tenancy.ITenanted`. The request-scoped
`ICurrentChurch` exposes nullable church/leader selection; `TenantContext` remains its mutable
implementation. This is distinct from `Tenancy.ITenantContext`, which requires a church ID:
`ApplicationDbContext` adapts request state through `ChurchIdOrThrow`. An options-only context
can build the model without a selected church, but queries against tenanted entities fail closed.
When authentication selection is wired, `LeaderId` denotes the church membership ID, not a global
Identity user ID.

The production domain sets are `ChurchMemberships`, `Contexts`, `People`, `Captures`,
`Touchpoints`, `Proposals`, `AuditEntries`, `LeaderTokens`, `Relationships`, `ContextLeaders`,
`ContextMembers`, `Attendances` and `PersonExternalRefs`. Their client-created Guid v7 IDs form
`(ChurchId, Id)` primary keys. Every root has a required, `Restrict` Church FK; references between
church-owned entities use the principal's composite primary key. Memberships uniquely pair
`(ChurchId, UserId)` and reference the unchanged global Identity user with `Restrict`.
All leader FKs use membership IDs, including touchpoint authors and hashed leader tokens.
Context custom-field definitions (`Name`, plain-string `Type`) are an owned JSON schema, not
tenant roots. Context leader/member rows retain `StartedOn`/`EndedOn` history. External IDs
exist only in `PersonExternalRefs`, unique by `(ChurchId, System, ExternalId)`.

Optional guardian and touchpoint-context references leave only their target ID nullable,
never `ChurchId`. All domain relationships use `Restrict` (SQL Server `NO ACTION`); deleting
a principal requires explicitly handling its dependents. Identity-only cascades are unchanged.
Capture status, relationship kind, proposal type and JSON payloads are storage fields, not
workflow implementations. Membership provisioning/authentication, token issuance/validation,
consent enforcement and connector execution remain separate work.

`20261106075754_DomainModelV1` adds this schema without changing the earlier Identity/Church
migrations. Scaffold migrations through the `Sheaf.AppHost` design-time factory, validate both
startup paths below, and run `Sheaf.Core.Tests.SqlServerMigrationTests` to apply the production
migration chain on the fixture's isolated SQL Server catalog (Docker/amd64 required).

Check model/snapshot parity through both configuration paths without creating a migration:

```pwsh
dotnet ef migrations has-pending-model-changes --project Sheaf.Data\Sheaf.Data.csproj --startup-project Sheaf\Sheaf.csproj
dotnet ef migrations has-pending-model-changes --project Sheaf.Data\Sheaf.Data.csproj --startup-project Sheaf.AppHost\Sheaf.AppHost.csproj
```

The backend check also needs `Auth:SigningKey`; supply local-only configuration for model checks.

The runtime options probes are in `Sheaf.Core.Tests.TenancyTests`; factory probes are in
`Sheaf.AppHost.Tests.DesignTimeAppDbContextFactoryTests`. These model-only tests do not connect
to a database or start Aspire.

The production SQL tests reuse the convention tests' assembly container but give every test
its own catalog, so normal TUnit parallel execution cannot reset another test's seed.
`ProductionTwoChurchSeed` migrates that catalog through `AddDatabase()` and seeds both churches,
global users and all 13 tenant roots. Isolation tests query every production tenant `DbSet`,
round-trip the owned context JSON, check SQL FK/uniqueness errors, and exercise bulk writes
and interceptor stamping without bypassing tenant filters. Respawn preserves
`__EFMigrationsHistory` and removes all other seed rows.

These container tests require Docker and an amd64-capable host (the SQL Server image is
amd64-only), including CI on `ubuntu-latest`. Run the migration and isolation cases with:

```pwsh
dotnet test --project Sheaf.Core.Tests\Sheaf.Core.Tests.csproj --no-restore --treenode-filter "/*/*/(SqlServerMigrationTests)|(SqlServerMigrationIsolationTests)/*"
```

For local Podman environments that need an explicit reachable container host, set
`$env:TESTCONTAINERS_HOST_OVERRIDE = "127.0.0.1"` in the test process only; no machine-level
configuration change is required.

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

## License

AGPL-3.0. See `LICENSE`.
