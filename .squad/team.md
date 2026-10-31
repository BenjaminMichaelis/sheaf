# Squad Team

> sheaf

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Architect | Lead | `.squad/agents/architect/charter.md` | ✅ Active |
| Backend | Backend Dev | `.squad/agents/backend/charter.md` | ✅ Active |
| Frontend | Frontend Dev | `.squad/agents/frontend/charter.md` | ✅ Active |
| AI-Integrations | AI & Integrations | `.squad/agents/ai-integrations/charter.md` | ✅ Active |
| Tester | Tester | `.squad/agents/tester/charter.md` | ✅ Active |
| Scribe | Decision Merger | `.squad/agents/scribe/charter.md` | 📋 Silent |
| Ralph | Work Monitor | `.squad/agents/ralph/charter.md` | 🔄 Monitor |
| Rai | RAI Reviewer | `.squad/agents/rai/charter.md` | 🛡️ RAI |
| Fact Checker | Devil's Advocate & Verification Agent | `.squad/agents/fact-checker/charter.md` | 🔍 Verifier |

## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage (adding missing tests, fixing flaky tests)
- Lint/format fixes and code style cleanup
- Dependency updates and version bumps
- Small isolated features with clear specs
- Boilerplate/scaffolding generation
- Documentation fixes and README updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear specs and acceptance criteria
- Refactoring with existing test coverage
- API endpoint additions following established patterns
- Migration scripts with well-defined schemas

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Multi-system integration requiring coordination
- Ambiguous requirements needing clarification
- Security-critical changes (auth, encryption, access control)
- Performance-critical paths requiring benchmarking
- Changes requiring cross-team discussion

## Issue Source

- **Repository:** BenjaminMichaelis/sheaf
- **Connected:** 2026-10-30
- **Filters:** open issues; labels P0/P1/P2, area:*, good-for-agent

## Project Context

- **Owner:** Benjamin Michaelis
- **Project:** Sheaf — church-leader follow-up without data entry
- **Description:** Sheaf helps church leaders follow up with people they meet without data entry. People can add themselves by QR with consent, share a phone contact, or provide a 15-second voice note; AI turns notes into records, deduplicates them, matches Planning Center, and prepares updates for a human to approve. Each leader’s people sync to phone contacts via CardDAV and are removed when the leader steps down. Small-group sentences become attendance, visitor, or care-need records; care is routed only to the leader and pastor. Leaders receive nudges and drafted texts that they send themselves; Sheaf never sends messages. Staff have dashboards and campaigns.
- **Stack:** .NET 10, ASP.NET Core, Aspire, SQL Server with per-church isolation, React PWA, Claude via Microsoft Foundry, Azure AI Speech, Planning Center integration, CardDAV, Azure Container Apps; later native Avalonia apps; AGPL-3.0.
- **Trust Rules:** AI drafts and people decide; consent is captured and “stop” applies globally; minors are parent/guardian contact only and receive no AI-drafted messages; sensitive details (health, abuse) never reach AI and are routed to the pastor; no spiritual scoring; full audit log of AI suggestions and approvals; per-church data isolation.
- **Created:** 2026-10-31
