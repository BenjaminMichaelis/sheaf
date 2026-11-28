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
header is not CSRF protection; the request-origin gate described below provides that protection.

### Authentication request security

Every POST, PUT, PATCH, and DELETE, including anonymous login and registration, must
provide a verifiable browser origin. An explicit `Origin` must match `AllowedOrigins`
or the API's effective scheme/host/port. Without `Origin`, `Sec-Fetch-Site: same-origin`
is accepted; if fetch metadata is also absent, the `Referer` origin is checked. An
explicit invalid/disallowed Origin or non-same-origin fetch metadata cannot be bypassed
with lower-priority headers. Unverifiable writes return 403 ProblemDetails. Safe methods
are unaffected. Manual/native clients must send an allowed `Origin`, not merely the
church-consistency header. Development permits HTTP(S) localhost origins at any port.

Outside Development, startup requires a nonempty `AllowedOrigins` array of explicit
HTTP(S) origins (no credentials, paths, queries, or fragments); there is no placeholder
fallback. Configure the actual browser frontend origins, including non-default ports.
The offline OpenAPI generator is exempt from the missing-origin startup check.

Identity uses the secure, HttpOnly `__Host-sheaf` cookie, with path `/`, no Domain,
and a 14-day sliding authentication lifetime. Remember-me cookies persist until their
expiry; non-remembered cookies remain session cookies. SameSite is None in Development
and Lax elsewhere. Existing cookies under the old name require a fresh login.

Incorrect passwords count toward account lockout; all failed logins return the same
401 response, including unknown and locked accounts. Login, registration, and church
switching share an IP-based, queue-free fixed-window limiter returning 429. Its counters
are in-memory and **per application instance**, not distributed across replicas.
These positive settings are tunable (defaults shown):

| Configuration key | Default |
| --- | --- |
| `Authentication:Lockout:MaxFailedAccessAttempts` | 5 |
| `Authentication:Lockout:Minutes` | 5 |
| `Authentication:RateLimit:PermitLimit` | 10 |
| `Authentication:RateLimit:WindowSeconds` | 60 |

Forwarded headers run first. The API accepts `X-Forwarded-For` and
`X-Forwarded-Proto` only from explicitly configured `ForwardedHeaders:KnownProxies`
(IP address array) or `ForwardedHeaders:KnownNetworks` (CIDR array). Both default to
empty, disabling forwarding rather than trusting arbitrary peers. Configure only your
deployment's actual trusted proxy addresses/ranges; no deployment trust range is guessed.
`ForwardedHeaders:ForwardLimit` defaults to 1 and must be positive. There is no reliance
on `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, and forwarded Host is not enabled. Forwarded
client IPs determine rate-limit partitions; the effective scheme determines the API
origin check. Configure `AllowedHosts` appropriately for your public API hosts.

### Production deployment requirements

Configure `AllowedOrigins` with the deployed browser origins and
`ForwardedHeaders:KnownProxies` and/or `ForwardedHeaders:KnownNetworks` with only the
deployment's trusted proxy addresses or ranges. Configure
`DataProtection:KeyVaultKeyIdentifier` with an HTTPS Azure Key Vault key identifier.
Outside Development and Testing, runtime startup requires this setting; offline OpenAPI
generation is exempt. The API persists its shared Data Protection key ring in the
`DataProtectionKeys` table, scoped to the application name `Sheaf`, and protects those
keys with Azure Key Vault through `ManagedIdentityCredential` (system-assigned managed identity only).
Grant that identity permission to get, wrap, and unwrap the configured key. Use a
versionless key identifier when enabling Key Vault key rotation.

First-administrator seeding is not yet defined; production provisioning must account
for creating the initial administrator.

Church-owned entities implement `Sheaf.Data.Tenancy.ITenanted`. EF query filters and
write guards enforce church ownership; queries require a selected church. These are
application-level protections, not database row-level security.

`BannedSymbols.txt` restricts filter bypass and tenant-context initialization (RS0030).
New exemptions require security review.

SQL-backed tests require a Docker-compatible container runtime and an amd64-capable
host for SQL Server. They use isolated, migrated catalogs.

### People and groups foundation

The backend tracker endpoints under `/api/church` persist church-owned People,
optional households, group types, groups, and dated group assignments: one interval in
which a Person held one role (`Member` or `Leader`) in a group. See `Sheaf/Tracker.http`
for requests.
Person `FirstName` and group names/starting instants are required; `LastName`,
contacts, households, and group types are optional. Display `Name` is the supplied
first name followed by the optional surname; names are never guessed or split.
`IsActive` is explicit lookup eligibility, not automatic inactivity or erasure. An
update that omits `IsActive` keeps the current value, and only `PeopleManage` holders
(Staff and Admin) may change it. Each Person records `CreatedAt` (defaulted by the
database for imports) and, when created through the API, `CreatedByUserId`. Neither is a
foreign key, so the audit survives account deletion and later duplicate review can tell
an original from a copy.

Staff and Admin create People, manage group types, create/update/end groups, and add,
end, or change the role of leaders.
What a group role allows within its group is mapped in `GroupRolePermissions`: a
current Leader may see the group, manage its Members, and record its meetings; a Member
role grants nothing, so being on a roster never gives an account access.
Leaders edit names and contacts only for people currently on their groups' rosters.
A Leader can add only people they can already see; adding a person to a group would
otherwise unlock that person's profile, so bringing someone new into a group is a Staff
action.
A person has at most one assignment in a group at any instant. Adding someone already in
the group returns 409; to promote or step down, `POST .../assignments/{id}/role` ends the
current assignment and starts one in the new role, so the history keeps both intervals.
Every role counts toward the roster used for absences. Leave/rejoin history is
supported. Ending a group retains it and all assignment intervals; group end bounds
their effective activity without rewriting history. There are no tracker delete or
automatic-merge endpoints.

Every guest becomes a Person, so duplicates are expected. Staff and Admin
(`PeopleManage`) review them: `GET /people/duplicates` lists pairs of active people
with the same full name, email or phone (phones compare digits only), earlier record
first, and `POST /people/duplicates/dismissals` marks a pair as different people so it
stops appearing. `POST /people/{survivorId}/merge` takes the survivor's version in
If-Match and the duplicate's id and version in the body. For last name, email, phone and
household, each field can be `Merge` (the default: the survivor's value, or the
duplicate's when the survivor's is blank), `Survivor` or `Duplicate`. First name keeps
the survivor's unless `Duplicate` is chosen. In one transaction, the merge:

- moves attendance to the survivor. Where both were at the same meeting, the earlier
  record stays and the other is taken off the list as history.
- moves group assignments. Overlapping intervals in the same group join into one
  (Leader if either was), and separate ones stay separate.
- moves the account link if only the duplicate has one. If both have one, it returns
  409 with `reason: bothLinkedToAccounts`.
- makes the survivor active if either person was.
- deletes any household it leaves empty.

The duplicate is kept as read-only history: inactive, with no household or account, and
with `mergedIntoId`, the time and the merging account recorded. Only Staff and Admin can
read it, and lookup, assignment, households and further merges treat it as gone. A merge
cannot be undone.

People, households and groups carry a `version`, returned in the body and as a strong
`ETag`. Updating a person, household or group, and ending a group, require that version
in `If-Match`: a missing header returns 428 and a version that is no longer current
returns 412, so a client cannot overwrite an edit it never saw; reload and retry.
A person's version also changes when they move household, a household's when it gains
or loses a member, and a group's when assignments are added or ended. Assignment
writes need no `If-Match`, but because they bump the group's version, two writes that
race on the same group (including ending it) are serialized and the loser gets 409.
Other write races and constraint failures also return 409.

Every list (`group-types`, `groups`, members and leaders history) is paged
in a stable order: pass `limit` (1–200, default 50) and `offset`, and read the next page
from `nextOffset`, which is null on the last page.

Leadership belongs to a Person, not a separate account access assignment. The optional
`Person.UserId` is only a future signup-link placeholder: non-null links are unique
per `(ChurchId, UserId)`. No API creates links, invitations, claims, or email/name
matches. **Leader access is not operational until signup integration creates an
explicit link.** Unlinked leaders fail closed for scoped resources. Access additionally
requires the account's persisted membership in that same church; a link alone or a
link in another church never grants access. Existing account/church role lifecycle
and cookie switching remain unchanged.

Roles form a ladder. An account holds one role per church, so each role includes
everything the role below it can do:

| Permission | Leader | Coordinator | Staff | Admin |
|---|---|---|---|---|
| `TrackerAccess`: use the tracker within the caller's own groups | ✓ | ✓ | ✓ | ✓ |
| `TrackerManage`: every group, group types, leadership | | | ✓ | ✓ |
| `PeopleManage`: every person, active state, resolving duplicates | | | ✓ | ✓ |
| `ManageMemberships`: which accounts use the church, with which role | | | | ✓ |

Coordinator behaves like a Leader until coordinator-specific scope is defined.

Authorization is split the way ASP.NET Core recommends. Which operations a role may
call is declared on the endpoints: every tracker route requires `TrackerAccess`,
creating a Person requires `PeopleManage`, and creating group types and groups,
updating or ending groups, and adding or ending leadership require `TrackerManage`.
Every permission check reads the selected membership's persisted role, not the role
claim, so a demotion or revocation applies on the next request rather than after the
cookie's five-minute revalidation. That read is shared with the tracker's current
actor, so each request reads the membership once. Which rows a caller reaches depends on loaded data, so it stays
in `TrackerScope`: SQL-translatable group/person scopes, household rules, and the
Staff-only `IsActive` change on a Person. History/concurrency protections remain in
application operations; HTTP problem responses are translated at the endpoint boundary.

Staff and leaders may manage whole households associated with accessible people,
including members outside their groups. A Leader may keep or remove a household's
existing members, but may add only people they can already see; a new household must
include at least one such person. Household responses expose identifiers and
first names, not full profiles or contacts; household authority does not grant
outside-group profile rights. An omitted/blank household label preserves the existing
label, or defaults to `The {LastName} Family` using the first submitted `MemberIds`
entry. If that person has no surname (or the list is empty), the label stays unset;
other members' surnames are not substituted. Explicit labels are editable and take
precedence.

Name lookup lets any tracker user find any active person in the church, so a Leader
can record who attended. It returns only the identifier, display name and optional
household label, never groups, contacts or household members. A search needs at least
two characters: two-character terms match the start of a first or last name (`Bo`
finds Bo, Bob and Bolton), and longer terms match anywhere in the full name. At most 20
results come back, with no paging, and each account may search 60 times a minute
(`Tracker:LookupRateLimit`) before receiving 429.
Unsafe tracker requests retain the `X-Sheaf-Church` stale-tab check.

Attendance and discipleship are deferred. Future attendance infers absence when a
rostered person does not attend; no separate absence workflow or visitor category
is introduced. Existing consent/trust requirements and Person erasure/anonymization
policy remain applicable; inactivity does not trigger erasure.

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

## License

AGPL-3.0. See `LICENSE`.
