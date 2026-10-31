# Project Context

- **Project:** sheaf
- **Created:** 2026-10-31

## Core Context

Agent Rai initialized and ready for work.

## Recent Updates

📌 Team initialized on 2026-10-31

## Learnings

Initial setup complete.

## Project Context (2026-10-30T23:26:27-07:00)

- **Owner:** Benjamin Michaelis
- **Project:** Sheaf helps church leaders follow up with people they meet without data entry. People can add themselves by QR with consent, share a phone contact, or provide a 15-second voice note; AI turns notes into records, deduplicates them, matches Planning Center, and prepares updates for a human to approve. Each leader’s people sync to phone contacts via CardDAV and are removed when the leader steps down. Small-group sentences become attendance, visitor, or care-need records; care is routed only to the leader and pastor. Leaders receive nudges and drafted texts that they send themselves; Sheaf never sends messages. Staff have dashboards and campaigns.
- **Stack:** .NET 10, ASP.NET Core, Aspire, SQL Server with per-church isolation, React PWA, Claude via Microsoft Foundry, Azure AI Speech, Planning Center integration, CardDAV, Azure Container Apps; later native Avalonia apps; AGPL-3.0.
- **Trust rules:** AI drafts and people decide; consent is captured and “stop” applies globally; minors are parent/guardian contact only and receive no AI-drafted messages; sensitive details (health, abuse) never reach AI and are routed to the pastor; no spiritual scoring; full audit log of AI suggestions and approvals; per-church data isolation.
