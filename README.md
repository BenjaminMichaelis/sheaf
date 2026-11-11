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

### Mobile (Avalonia)

`Sheaf.Mobile*` holds the shared app plus Android, iOS, Browser and Desktop heads. Install workloads with `dotnet workload restore`. Android is built locally; iOS requires macOS (built in CI). Mobile apps run separately from Aspire; point them at the API (Android emulator: `http://10.0.2.2:5285`, iOS simulator: `http://localhost:5285`).

iOS CI selects preinstalled Xcode 27.2 and pins both the .NET SDK and iOS workload set to 10.0.404, matching [Microsoft's Xcode 27.2 release](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.2.10000). The iOS job overrides SDK resolution in its checkout only; the shared `global.json` and Android/backend toolchains remain unchanged.

## License

AGPL-3.0. See `LICENSE`.
