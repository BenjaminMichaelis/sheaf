# sheaf
Leader-first relational capture for churches: capture the people you meet, keep 'my people' in your phone, and follow up with AI drafts you send yourself. AI for Church Hackathon 2026.

## Development

Prerequisites: .NET SDK (see `global.json`), Docker, and the [Aspire CLI](https://aspire.dev).

```pwsh
aspire run          # API, SQL Server, migrations and the React frontend; API at http://localhost:5285
```

The React + Vite PWA lives in `Sheaf.Web` (requires Node and pnpm; Aspire runs `pnpm install` and `pnpm dev`, proxying `/api` to the backend).

Backend-only build/test: `dotnet build Sheaf.Backend.slnf`, then `dotnet test --project <test csproj>`.

### Isolated development SQL storage

For a worktree that needs a fresh development database, choose a stable, Docker-safe
suffix and reuse it on every start:

```pwsh
aspire start --non-interactive --isolated --apphost Sheaf.AppHost\Sheaf.AppHost.csproj -- --Sheaf:ContainerSuffix=bmichaelis-reimagined-telegram --DisableDbGate=true
```

This scope uses container `Sheaf-bmichaelis-reimagined-telegram-sql` and persistent
volume `Sheaf-bmichaelis-reimagined-telegram-database`, with catalog `Sheaf-db`.
The existing migration resource applies the current migrations before the backend starts.
`--isolated` isolates Aspire's ports/secrets, not explicitly named Docker volumes;
`Sheaf:ContainerSuffix` scopes both the SQL container and its volume. DBGate is
disabled here because its optional persistent container has a shared fixed name.
The backend's explicitly configured port `5285` must still be free.
Without a suffix (or with whitespace), the existing `Sheaf-sql` / `Sheaf-database`
names are unchanged. Azure publish settings are unaffected.

Use the same suffix to retain this worktree's database across restarts. Choosing a
previously unused suffix creates new storage; it does not repair or delete the old
database, its migration history, or its volume. Do not mount the same SQL data volume
into multiple SQL containers. Stop this AppHost with
`aspire stop --non-interactive --apphost Sheaf.AppHost\Sheaf.AppHost.csproj`;
persistent SQL storage/container are intentionally retained (no force/reset needed).

### Regenerating API types

Requires the .NET SDK pinned in `global.json`, Node.js (24 in CI), and pnpm
(`pnpm@11.1.3`, pinned in `Sheaf.Web\package.json`). From the repository root:

```pwsh
Set-Location Sheaf.Web
pnpm install --frozen-lockfile
pnpm run gen:api       # npm run gen:api also works after dependencies are installed
```

The command builds the API in Release, exporting the producer-owned
`Sheaf\openapi\v1.json` document and generating
`Sheaf.Web\src\api\schema.d.ts` from it. Commit both generated files after API
contract changes; do not edit them by hand. Normal builds also export the spec.
The exporter runs with entry assembly `GetDocument.Insider`; only that generation
process uses a non-secret local connection string to register database services
without connecting to a database. Runtime startup still requires its configured
database connection. No running Aspire app or database is needed for export.
CI regenerates both artifacts and fails if either differs or is untracked.

### Database tenancy

Runtime `AddDatabase()` and `DesignTimeAppDbContextFactory` both use `Sheaf.Data`'s
`AddSheafDatabase()` to configure Azure SQL, the tenancy convention and write interceptors, and
Identity schema version 3. `ApplicationDbContext` refuses to initialize without `UseMultiTenancy()`.
Tenancy integration tests resolve the real context through `AddDatabase()` and apply the
`Sheaf.Data` migrations to isolated SQL Server catalogs.

Church-owned entities implement only `Sheaf.Data.Tenancy.ITenanted`. The request-scoped
`ICurrentChurch` and `TenantContext` expose read-only, nullable church/leader selection.
Only authentication's `ChurchAccessService` and application-cookie events call
`ITenantContextInitializer`; changing churches while tenanted entities are tracked is rejected
without discarding tracked work. This is distinct from `Tenancy.ITenantContext`, which requires a church ID:
`ApplicationDbContext` adapts request state through `ChurchIdOrThrow`. An options-only context
can build the model without a selected church, but queries against tenanted entities fail closed.
`LeaderId` denotes the church membership ID, not a global Identity user ID. The non-pooled,
scoped context reads this selection without depending on the authentication services.

### Church-aware Identity sessions

Fresh login and registration use Identity's validated account, not an incoming/provider principal.
Exactly one church membership selects that church; zero or multiple memberships leave the account
authenticated without a church. The cookie contains a coherent `cid` (church ID), `cmid` (membership ID),
and `role` tuple only for a selected membership. `role` is Identity's configured role claim type and
comes from the membership, never persisted user role/church claims. Canonical Identity account and
security-stamp claims are retained. Malformed, partial, duplicate, empty-ID or invalid-role church
tuples reject the cookie rather than selecting a church.

Application-cookie validation initializes request selection before invoking Identity's security-stamp
validator, then synchronizes from its final principal (or clears selection on rejection). At the
five-minute validation interval, a selected membership is rechecked through the normal tenancy filter
and its role rebuilt. Removal clears selection even without a security-stamp change. Unselected
sessions stay unselected on every renewal, including when exactly one different membership remains;
only a fresh sign-in auto-selects. Cookie and stamp validation share the DI `TimeProvider`, allowing
tests to advance beyond the interval without sleeping.

`POST /api/auth/login`, `POST /api/auth/register` and `GET /api/auth/user` return the existing account
fields plus `churches` and nullable `currentChurch`. Each church contains `churchId`, `churchName`
and a string `role` (`Leader`, `Coordinator`, `Staff` or `Admin`). Available churches are read for the
validated account only; caller-supplied user IDs are not accepted. Anonymous user responses have an
empty church list and no current church, without querying the directory.

`ChurchMembershipDirectory` is the sole production named `TenancyFilter` read bypass and is called
only by `ChurchAccessService`. It projects church ID/name/role, keeps other named filters enabled,
and records structured `ChurchMembershipDirectoryRead` audit event **15600** with `UserId` and
`ChurchCount`. Selection additionally rechecks the membership's ID and role under normal filters.
This establishes session/context state, not the church policies or switch endpoint tracked separately
in #157/#158. API cookie challenges retain HTTP 401/403 rather than login/access-denied redirects.

Both `IgnoreQueryFilters` overloads and `ITenantContextInitializer.Initialize` are listed in
`BannedSymbols.txt` and enforced in the `Sheaf`, `Sheaf.Core` and `Sheaf.Data` projects by
`Microsoft.CodeAnalysis.BannedApiAnalyzers` (RS0030, a build error). The directory, `ChurchAccessService`
and `ChurchCookieEvents` hold the only `#pragma warning disable RS0030` exemptions; new exemptions
require security review.

### Tenancy write guards

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
Known limits: in such a global write, tenanted rows reached only through navigations are read
unfiltered and can influence which global rows change; and
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

The runtime options probes are in `Sheaf.Core.Tests.TenancyTests`; factory probes are in
`Sheaf.AppHost.Tests.DesignTimeAppDbContextFactoryTests`. These model-only tests do not connect
to a database or start Aspire.

The SQL Server tenancy tests verify membership query isolation, missing-tenant rejection,
automatic church stamping, immutable ownership, cross-church tracked-write rejection, and
tenant-filtered bulk updates/deletes, including rejection of filter bypasses and ownership
updates through membership `Union`/correlated sources. They also verify the migrated membership
foreign keys, restricted principal deletion, and per-church user uniqueness. The shared container fixture
starts SQL Server only; it creates no separate test schema.

There are no synthetic tenancy entities, contexts, or migrations. Owned JSON, tenanted
domain-to-domain relationships, and navigation-based bulk-write shapes are not covered by this
branch's minimal membership model; they need coverage when real application entities use them.

These container tests require Docker and an amd64-capable host (the SQL Server image is
amd64-only), including CI on `ubuntu-latest`. Run the migration and isolation cases with:

```pwsh
dotnet test --project Sheaf.Core.Tests\Sheaf.Core.Tests.csproj --no-restore --treenode-filter "/*/*/(SqlServerMigrationTests)|(SqlServerProductionTenancyTests)|(SqlServerBulkUpdateCompatibilityTests)/*"
```

For local Podman environments that need an explicit reachable container host, set
`$env:TESTCONTAINERS_HOST_OVERRIDE = "127.0.0.1"` in the test process only; no machine-level
configuration change is required.

The church-session tests use the same SQL Server container fixture and isolated migrated catalogs,
with real Identity cookies and controlled time, through
[TUnit's ASP.NET Core integration](https://tunit.dev/docs/examples/aspnet.md).
They also pin compiled directory/initializer call sites. Run them with:

```pwsh
dotnet test --project Sheaf.Api.Tests\Sheaf.Api.Tests.csproj --treenode-filter "/*/*/(ChurchSessionTests)|(ChurchAuthenticationArchitectureTests)/*"
```

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

## License

AGPL-3.0. See `LICENSE`.
