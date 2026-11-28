# Sheaf demo POC plan

**Goal:** a 5–7 minute demo of the whole Sheaf idea, built on what's on `main`, that a pitch audience can watch end to end:
a leader meets someone → captures them in seconds → AI turns the note into proposals → the leader approves →
the person is in "My people" with a nudge and an AI-drafted text the leader sends themself → Staff see the funnel and the gaps.

**Ground rules (from your answers)**
- Audience: demo / pitch. Polish what's on screen; fake what isn't.
- Build on `main`, cut corners: reuse auth, tenancy, people, groups and attendance. Few tests in new code. Anything worth keeping gets rebuilt properly through the real issues later.
- Real Claude for extraction, drafts and the weekly brief. Everything else external is faked.
- Scope: capture → AI → approve, My people + follow-up, groups + attendance UI, staff dashboards.

**Working alongside your Planning Center work**
- The POC lives on branch `poc/demo` in its own worktree, cut from `main`. It never merges to `main` as a whole.
- It doesn't touch Planning Center code. Planning Center appears only as seeded people labeled "from Planning Center" (simulated).
- New backend code goes under `Sheaf/Poc/` and `Sheaf.Data/Poc/`, with one POC migration. When `main` moves (for example your Planning Center migrations), merge `main` into `poc/demo` and regenerate the POC migration.

---

## Demo script (what we build toward)

Adapted from #68. Each beat is a slice below.

1. **Welcome week.** Three leaders each meet the same freshman. One uses quick add, one types a note, one dictates a voice note. Claude extracts the people. The approval queue shows "Is this the same Maya?" and merges them into one person.
2. **Follow-up.** Maya is in Jake's "My people" with a "first contact within 48 hours" nudge. Jake taps it, gets an AI-drafted text ("Hey, it's Jake from the welcome table…"), and Send opens his phone's Messages app. A touchpoint is logged.
3. **Small group night.** The leader records attendance from the roster, adds a visitor in the same screen, and the group view shows the 8-week trend. A drifting member gets a "missed 2 meetings" nudge.
4. **Staff.** The funnel (captured → contacted → attended → in a group), follow-up gaps, quiet groups, and a Claude-written weekly brief over aggregates only.
5. **Trust.** A note mentioning a health crisis is redacted before Claude sees it and routed to a pastor inbox. "Don't contact" blocks drafts for every leader. No drafts for minors. Every AI output is labeled "AI-drafted", and the audit log shows what AI proposed vs. what a human approved.

---

## Slices (in build order; each ends in something demoable)

### 0. Foundation
- Branch `poc/demo` and worktree; check `aspire run` works.
- **Demo seeder** (#24, simplified): one college-ministry church, 6 leaders + 1 staff + 1 admin with known demo logins, ~120 students (40 "from Planning Center"), 4 groups with 6 weeks of attendance including drifting members, planted duplicates, an Easter family with a minor. Bogus with a fixed seed; a "Reset demo" endpoint for Development.
- **AI client** (#28, simplified): `IChatClient` (Microsoft.Extensions.AI) for Claude, key as an Aspire secret parameter. A canned fake client when no key is set, so the app still runs for anyone.
- **Web shell** (#33, simplified): phone-first layout, bottom nav for leaders (Home, Capture, My people, Groups, Review), `/staff` area, church switcher.

### 1. Groups + attendance UI (uses the existing API; validates the frontend patterns first)
- My groups list, group page with roster (#54).
- Record attendance: tap who came, person search with "Create new person" at the end, same-day warning, confirm-duplicate dialog (#55).
- Group view: last 8 meetings, per-member attendance, recent visitors (#56).

### 2. Capture → AI → approve
- New POC tables: `Capture` (text, source, status, extraction JSON), `Proposal` (type + JSON payload, status, reasons), `AuditEntry`.
- **Quick add** (#119): name, phone, how we met, under-18 toggle (guardian contact, no minor phone). Creates a Person and a Capture.
- **Text note** (#37) and **voice note** (#38) using the browser's built-in speech recognition (not Azure Speech). Transcript is editable before submit.
- **Sensitive pre-filter** (#41): keyword/regex detector runs before any Claude call; hits are redacted and the original goes to a Staff-only pastor inbox.
- **Extraction** (#29, #42): Claude structured output → people, details, attendance hints, needs, minor suspected. Runs in a simple in-process background queue (no Hangfire); the UI polls "processing…".
- **Dedupe** (#43): exact phone, exact email, then fuzzy name + context, against seeded and imported people → "same person?" proposals.
- **Approval queue** (#45): cards with approve / edit / reject and "why suggested" showing the source text. Approving applies the change and writes an audit entry (#46).

### 3. My people + follow-up
- **My people** (#51): members of groups I lead + people I captured and haven't been placed in a group yet (simplified #19/#50). Shows last touchpoint and next step.
- **Touchpoints** (#57): log manually (texted, coffee, came to something); approvals create them too.
- **Nudges** (#58): computed when the list loads, not by a daily job: first contact within 48 hours, no touchpoint in N days, missed 2 group meetings.
- **Drafted texts** (#59): Claude drafts in the leader's voice; Send opens `sms:` or copies. Labeled "AI-drafted". Never for minors or "Don't contact".
- **Don't contact** (#60, partial): one tap, blocks drafts for everyone, hides from My people nudges.
- **Contacts sync, faked** (#52/#53): "Add to my phone" downloads a vCard. A settings screen mocks the CardDAV setup.

### 4. Staff dashboards
- **Funnel** (#62): captured → contacted → attended → in a group, filterable by leader and group.
- **Follow-up gaps and group health** (#63): never-contacted, overdue, unplaced people; quiet groups from attendance trends.
- **Weekly brief** (#64): Claude narrative over aggregates only, with links. No scoring individuals.
- **Audit log** page and **pastor inbox**.
- **Responsible AI page** (#65, short version): what the AI does and never does, where data goes.

### 5. Demo polish
- Write the click-by-click demo script against the seeded data, rehearse it, fix the rough spots.
- Run locally and record; no deployment.

---

## Faked or out of scope

| Area | In the POC | Real work stays in |
|---|---|---|
| Planning Center | Seeded people labeled "from Planning Center"; "Would be written to Planning Center" badge on approvals | Your real integration: #21–23, #44, #47, #48, #113, #116–118 |
| CardDAV contacts sync | vCard download + mocked setup screen | #26, #52, #53 |
| Voice transcription | Browser speech recognition | #27 (Azure Speech) |
| Sign-in | Existing local sign-in with demo accounts | #18 invites, #114 Google, #184 email verification |
| Background jobs | In-process queue, nudges computed on read | #16, #40 |
| QR self-entry form | Not built | #34–36 |
| Native/mobile client apps | Not built (phone-sized web app) | #70–75 |
| CSV import, export/delete, rollover/handoff, hide-from-lookup, church.id in logs | Not built | #115, #61, #50, #120, #183, #32 |

**Corners we cut:** minimal tests, coarse role checks (Leader vs Staff) on new endpoints, no If-Match on new endpoints, simplified visibility rules.
**Corners we don't cut**, because they are the pitch: tenancy (automatic anyway), redaction before Claude, no drafts to minors or "Don't contact", AI-drafted labels, no automatic sending.

---

## Issue cleanup (separate from the POC, you decide)
- Close as done: #17 (membership/roles/cid shipped in #166–169), #20 (domain model shipped differently in #124/#180/#185). #19 is largely covered by `TrackerScope`; close or re-scope.
- Hackathon #9, #30, #65–69: the submission deadline (Nov 25) has passed. Close, or re-scope #68 as "POC demo video".
- Re-label the rest so the backlog shows what's next after the POC.

## Decisions (2026-11-27)
1. **Claude access:** the Anthropic API directly through the official `Anthropic` C# SDK, model `claude-opus-5-5`. The key is an Aspire secret parameter (`anthropic-api-key`) stored in AppHost user secrets, never in the repo. Without a key the app uses canned AI responses.
2. **Demo runs locally** and is recorded; no deployed environment (#66 stays out of the POC).
3. **College ministry story** (welcome week, dorms, small groups), matching the seeder plan.

## Build notes

- **Slice 2:** notes are processed inline (the request waits while the AI reads the note) instead of in a background
  queue; the capture screen shows a progress bar. Without an API key a rule-based fallback extractor stands in for Claude
  and the result screen says so. Leaders see that a duplicate exists and why, but not the other person's phone or email.
  Merging archives the duplicate (IsActive = false) rather than moving its attendance history.
- **Slice 3:** nudges are computed when the list loads: first contact within 48 hours of being added (overdue turns
  red), absent from the group's last two meetings, and no touchpoint for 14 days. Adding someone logs a "Met"
  touchpoint. "Open Messages" uses an `sms:` link and "Copy" uses the clipboard; both log an AI-drafted touchpoint.
  "Add to my phone" downloads a vCard; the CardDAV settings mock was skipped. Drafts are refused server-side for minors
  and Don't contact. A guardian the leader adds also appears in My people, since the leader created them.
- **Slice 4:** the staff overview is computed on read: a 30-day funnel for people leaders added (met, followed up, came
  to a group, joined a group; filterable by leader), gap counts, a list of people waiting for a first text (minors
  excluded), group health (last 4 meetings vs the 4 before: Growing, Steady, Slipping, No recent meetings) and per-leader
  counts. The weekly brief is generated on request and sends only totals, group names and leader first names to the
  AI. "How Sheaf uses AI" is in the account menu for leaders and staff. The seed now has a slipping sophomore group, a
  growing women's study, and one of this week's captures visiting each group (two joined).
- **Slice 5:** the click-by-click script is [demo-script.md](demo-script.md), rehearsed against the seeded data. Fixes
  from rehearsal: quick add now confirms where the person went and offers to open them; a possible match reads "Is this
  the same Maya Chen?"; approving a match logs the approver's own "Met" touchpoint; drafts for someone not yet in a
  group invite them to the leader's group; drifting-member names read "Dana, Elmer and Lyle"; a "Maya at the fair"
  sample note drives beat 1.
