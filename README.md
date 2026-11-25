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

Authenticated accounts can select a member church with `POST /api/auth/church/{churchId}`.
The target membership is rechecked before a new Identity cookie is issued; inaccessible
and nonexistent churches both return 403. A switch is refused with 409 while tenanted
entities are tracked by the request context.

The Identity cookie represents one active church and is shared by all browser tabs.
Unsafe requests under `/api/church` must include `X-Sheaf-Church` with the church ID
currently shown by the client, in `D` Guid format. Missing, malformed, or mismatched
values return 409 ProblemDetails; safe methods do not require the header. After a
successful switch, clients should update their shared cookie state and reload stale tabs.
The check rejects stale-tab writes whose cookie and displayed church disagree; it does not
cancel requests already in flight or serialize concurrent cookie updates. This consistency
header is not CSRF protection; CSRF protections are tracked separately in #160.

Church-owned entities implement `Sheaf.Data.Tenancy.ITenanted`. EF query filters and
write guards enforce church ownership; queries require a selected church. These are
application-level protections, not database row-level security.

`BannedSymbols.txt` restricts filter bypass and tenant-context initialization (RS0030).
New exemptions require security review.

SQL-backed tests require a Docker-compatible container runtime and an amd64-capable
host for SQL Server. They use isolated, migrated catalogs.

### People and groups foundation

The backend tracker endpoints under `/api/church` persist church-owned People,
optional households, group types, groups (`Context` of kind `Group`), and dated
Person membership/leadership intervals. See `Sheaf/Tracker.http` for requests.
Person `FirstName` and group names/starting instants are required; `LastName`,
contacts, households, and group types are optional. Display `Name` is the supplied
first name followed by the optional surname; names are never guessed or split.
`IsActive` is explicit lookup eligibility, not automatic inactivity or erasure.
The unreleased name-schema migration requires an empty People table and fails
rather than discard unexpected legacy records; it is not a deployed-data conversion.

Staff create People, manage group types, create/update/end groups, and assign leaders.
Leaders manage membership and edit profiles only within their currently led groups.
A Leader can add only people they can already see; adding a person to a group would
otherwise unlock that person's profile, so bringing someone new into a group is a Staff
action. Multiple simultaneous assignments and leave/rejoin history are supported. Ending a
group retains it and all assignment intervals; group end bounds their effective
activity without rewriting history. Every assignment write bumps the group's version,
so a write that races another change to the same group (including ending it) is
rejected with 409 and should be retried. Other write races and constraint failures
also return 409. Requests do not yet carry a client version or `If-Match`, so this does
not protect against a client overwriting an edit it never saw. There are no tracker
delete or automatic-merge endpoints.

Leadership belongs to a Person, not a separate account access assignment. The optional
`Person.UserId` is only a future signup-link placeholder: non-null links are unique
per `(ChurchId, UserId)`. No API creates links, invitations, claims, or email/name
matches. **Leader access is not operational until signup integration creates an
explicit link.** Unlinked leaders fail closed for scoped resources. Access additionally
requires the account's persisted membership in that same church; a link alone or a
link in another church never grants access. Existing account/church role lifecycle
and cookie switching remain unchanged. A typed current-church actor boundary resolves
HTTP identity against persisted membership afresh for each tracker operation; actor
snapshots are not durable authorization grants. The route-level `TrackerAccess`
permission is a cheap gate on cookie claims; the service re-checks the persisted role
because claims can be up to five minutes stale. ASP.NET Core resource policies enforce
tracker role eligibility and Staff-only operations. SQL-translatable group/person scopes,
household rules, and history/concurrency protections remain in application operations;
HTTP problem responses are translated at the endpoint boundary.

Staff and leaders may manage whole households associated with accessible people,
including members outside their groups. A Leader may keep or remove a household's
existing members, but may add only people they can already see; a new household must
include at least one such person. Household responses expose identifiers and
first names, not full profiles or contacts; household authority does not grant
outside-group profile rights. An omitted/blank household label preserves the existing
label, or defaults to `The {LastName} Family` using the first submitted `MemberIds`
entry. If that person has no surname (or the list is empty), the label stays unset;
other members' surnames are not substituted. Explicit labels are editable and take
precedence. Same-church active lookup returns only display name, current group names,
optional household label and household first names.
Unsafe tracker requests retain the `X-Sheaf-Church` stale-tab check.

Attendance and discipleship are deferred. Future attendance infers absence when a
rostered person does not attend; no separate absence workflow or visitor category
is introduced. Existing consent/trust requirements and Person erasure/anonymization
policy remain applicable; inactivity does not trigger erasure.

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

## License

AGPL-3.0. See `LICENSE`.
