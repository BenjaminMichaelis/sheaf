# sheaf
Leader-first relational capture for churches: capture the people you meet, keep 'my people' in your phone, and follow up with AI drafts you send yourself. AI for Church Hackathon 2026.

## Development

Prerequisites: .NET SDK (see `global.json`), Docker, and the [Aspire CLI](https://aspire.dev) 17+ (the AppHost build uses the CLI bundle; it falls back to `dnx` when `aspire` is not on `PATH`).

```pwsh
aspire run          # API, SQL Server, migrations and the React frontend; API at http://localhost:5285
```

The React + Vite PWA lives in `Sheaf.Web` (requires Node and pnpm; Aspire runs `pnpm install` and `pnpm dev`, proxying `/api` to the backend).

Backend-only build/test: `dotnet build Sheaf.Backend.slnf`, then `dotnet test --project <test csproj>`.

For separate worktree SQL storage, use a unique, stable container suffix:

```pwsh
aspire start --non-interactive --isolated --apphost Sheaf.AppHost\Sheaf.AppHost.csproj -- --Sheaf:ContainerSuffix=local-dev --DisableDbGate=true
```

Reuse the suffix to retain the database; a new suffix creates fresh storage.
`--isolated` alone does not isolate named SQL volumes. DBGate is disabled to avoid
its shared container name; backend port `5285` must still be free.

### Regenerating API types

From the repository root, using the SDK and pnpm versions pinned in the repository:

```pwsh
Set-Location Sheaf.Web
pnpm install --frozen-lockfile
pnpm run gen:api
```

Commit the generated `Sheaf\openapi\v1.json` and `Sheaf.Web\src\api\schema.d.ts`
after API contract changes; do not edit them by hand. No running app or database
is needed. CI checks that both artifacts are current.

### Authentication and tenancy

Identity cookies authenticate accounts; a church is selected automatically at sign-in
only when the account has exactly one membership. Authentication alone does not grant
access to a church.

Church-owned entities implement `Sheaf.Data.Tenancy.ITenanted`. EF query filters and
write guards enforce church ownership; queries require a selected church. These are
application-level protections, not database row-level security.

`BannedSymbols.txt` restricts filter bypass and tenant-context initialization (RS0030).
New exemptions require security review.

SQL-backed tests require a Docker-compatible container runtime and an amd64-capable
host for SQL Server. They use isolated, migrated catalogs.

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

## License

AGPL-3.0. See `LICENSE`.
