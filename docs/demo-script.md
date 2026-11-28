# Sheaf demo script

A 6–7 minute walk through the whole idea, click by click, against the seeded demo data. The story: a college campus
ministry ("Northgate Church · Campus Ministry") at the end of welcome week.

## Before you start

1. **Run the app** on branch `poc/demo`:
   ```powershell
   aspire start --non-interactive --isolated --apphost Sheaf.AppHost\Sheaf.AppHost.csproj -- --Sheaf:ContainerSuffix=poc-demo --DisableDbGate=true
   ```
   Open the frontend URL that `aspire describe` lists (the port changes each start; the backend is always `5285`).
2. **Claude key (optional but recommended).** Without it everything still works, with a rule-based fallback and an
   "AI: canned" chip in the top bar. With it, the chip says "Claude" and notes, drafts and the brief come from Claude:
   ```powershell
   dotnet user-secrets set "Parameters:anthropic-api-key" "<your key>" --project Sheaf.AppHost
   ```
3. **Reset the demo data** right before presenting: any account menu (top-right avatar) → **Reset demo data**. This
   rebuilds everything and signs everyone out. Dates are relative to today, so the nudges read the same any day.
4. **Phone-sized window.** In Chrome or Edge, open DevTools → device toolbar → a phone around 390 px wide, or just
   narrow the window. Voice notes need Chrome or Edge (the browser's own speech recognition).
5. **Switching people** is part of the story: account menu → **Sign out** lands on the sign-in page, where every demo
   account is one tap. Password for all of them, if asked: `Sheaf-demo-2026!`.

## Cast

| Who | Role | Why they're in the demo |
|---|---|---|
| Maria Okafor | Leader, Thursday Women's Study | Meets Maya at the welcome table |
| Priya Shah | Leader, co-leads the women's study | Meets Maya again at the activities fair |
| Jake Morales | Leader, Smith Hall Freshman Guys | Group night, drifting members, a planted duplicate |
| Ruth Daniels | Staff, campus pastor | Dashboards, pastor inbox, audit log |

---

## 1. Welcome week: capture in seconds (≈1:30)

> "Leaders meet dozens of students in welcome week, and most of those names end up on napkins. Sheaf makes capture
> take seconds."

1. Sign in as **Maria Okafor**. Point out the home screen: *Add someone you met*, *Follow up today*, her group.
2. Tap **Add** (bottom nav) → **Quick add**. Enter *Maya* / *Chen*, phone *(312) 555-0420*, how you met *Welcome table*.
   Tap **Add**.
   - "Maya Chen is in My people. You'll get a reminder to reach out within 48 hours."
3. Sign out → sign in as **Priya Shah**. Tap **Add**. Under *Try a sample*, tap **Maya at the fair** (or tap **Speak**
   and say it). Tap **Read my note**.
   > "Priya met Maya too, at the activities fair. She doesn't know Maria already added her."
   - The card says **Is this the same Maya Chen?** Why suggested: *Same phone number · Same name*. It shows the words
     from the note it came from, and the existing record (added by Maria at the welcome table). It does not show
     Maria's copy of Maya's phone: leaders see that a match exists and why, not someone else's contact details.
   - Point at the *AI-suggested* chip: nothing is saved until a person approves.
4. Tap **Same person, update**.
   > "One Maya, not two. Priya's details (freshman, Alder Hall, the invite to Thursday study) are merged into Maria's record."

## 2. Follow-up: the 48-hour nudge and a drafted text (≈1:30)

1. Sign out → sign in as **Maria**. Tap **People** → the **New** filter → **Maya Chen**.
   - Nudge: *New: reach out within 48 hours*. Next step: *Invite her to Thursday women's study* (from Priya's note).
   - Touchpoints show **Met** by Maria (welcome table) and **Met** by Priya (activities fair).
2. Tap **Draft a text** → **Invite to group**.
   - The draft is in Maria's voice and invites Maya to Thursday Women's Study. It's labeled *AI-drafted*. Edit a word
     to show it's hers to change.
3. Tap **Open Messages** (on a phone this opens Messages with the text filled in; on a laptop use **Copy**).
   - A *Texted · AI-drafted* touchpoint is logged and the nudge clears.
   > "Sheaf never texts anyone. The leader sends it from their own phone, in their own name."

## 3. Small group night (≈1:15)

1. Sign out → sign in as **Jake Morales**. Home shows **1 suggestion to review** and his follow-ups.
2. Tap **Groups** → **Smith Hall Freshman Guys**.
   - The orange card: *Check in with Dana, Elmer and Lyle. They have each missed the last 2 meetings.*
   - The bar chart of the last 6 meetings, the visitors (one came twice: *Invite to join?*), the roster with attendance.
3. Tap **Record tonight's attendance** → **Everyone** → untap two people → tap a visitor chip under *Anyone else?*.
   Tap **Save · N here**. The trend gains tonight's bar.
4. Tap **Review** (bottom nav). *Is Chris Nguyen the same person as Christopher Nguyen?* — Jake added "Chris" at
   move-in; Christopher came from Planning Center. *Same phone number; "Chris" can be short for "Christopher".*
   Tap **Merge**.
   > "Duplicates get caught when they're created, and a person decides."

## 4. Staff: where to help this week (≈1:15)

1. Sign out → sign in as **Ruth Daniels** (lands on the staff **Overview**).
2. Tap **Write this week's brief**. Three short paragraphs: outreach, where follow-up is slipping, group health.
   > "The brief is written from totals and group names only. No student names or notes are sent to AI."
3. Scroll: the **funnel** for the last 30 days (met → followed up → came to a group → joined), the **gap tiles**, who is
   **waiting for a first text**, **Groups** (Sophomore Co-ed is *Slipping*, Thursday Women's Study is *Growing*) and
   **Leaders** (counts, never scores). Pick a leader in the funnel's dropdown to narrow it.

## 5. Trust (≈1:00)

1. Still as Ruth, open the account menu → **Leader view** → **Add**. Tap the sample **Something sensitive** → **Read my
   note**.
   - "A sentence about health was kept from AI and sent privately to your campus pastor." Only *Jordan Ellis* is
     suggested; the line about her mom's diagnosis never left the server.
2. Account menu → **Staff view** → **Inbox**: the original note, marked *health*, visible only to staff.
3. **Minors.** Leader view → **Add** → sample **A high schooler** → **Read my note**. The card is flagged *Under 18*:
   no phone, no email, no AI drafts; contact goes through a parent or guardian.
4. **Don't contact.** Open anyone in **People** → **Don't contact** → confirm. Drafts and nudges stop for every leader.
5. Staff view → **Audit**: every AI suggestion, approval, edit, draft and redaction, tagged *AI* and *Edited by a
   person*.
6. Account menu → **How Sheaf uses AI**: what it does, what it never does, where data goes.
   > "AI does the remembering and the first draft. People make every decision."

---

## If something goes wrong

- **A screen looks stale**: pull to refresh / reload; every list is computed on load.
- **Ran a beat twice**: account menu → **Reset demo data**, then sign in again.
- **Voice doesn't start**: use the sample chip; dictation needs Chrome or Edge and microphone permission.
- **"AI: canned" when you expected Claude**: the key isn't set, or the app wasn't restarted after setting it.
- **Claude declines or errors**: the app falls back to the canned result for that request and keeps going.
