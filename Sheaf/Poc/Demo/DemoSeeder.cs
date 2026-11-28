using Bogus;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data;
using Sheaf.Data.Poc;

using Person = Sheaf.Data.Person;

namespace Sheaf.Poc.Demo;

public sealed record DemoAccount(string Email, string FirstName, string LastName, ChurchMembershipRole Role, string Blurb);

/// <summary>Fixed demo church, logins and story data. Development only.</summary>
public static class DemoData
{
    public const string ChurchSlug = "northgate-demo";
    public const string ChurchName = "Northgate Church · Campus Ministry";
    public const string TimeZoneId = "America/Chicago";

    /// <summary>Shared password for the demo logins. Test-only value; the demo database is local.</summary>
    public const string Password = "Sheaf-demo-2026!";

    public static readonly DemoAccount[] Accounts =
    [
        new("jake@demo.sheaf.test", "Jake", "Morales", ChurchMembershipRole.Leader, "Leads Smith Hall freshman guys"),
        new("devon@demo.sheaf.test", "Devon", "Price", ChurchMembershipRole.Leader, "Co-leads Smith Hall freshman guys"),
        new("maria@demo.sheaf.test", "Maria", "Okafor", ChurchMembershipRole.Leader, "Leads Thursday women's study"),
        new("priya@demo.sheaf.test", "Priya", "Shah", ChurchMembershipRole.Leader, "Co-leads Thursday women's study"),
        new("sam@demo.sheaf.test", "Sam", "Whitfield", ChurchMembershipRole.Leader, "Leads grad & young professionals"),
        new("ellie@demo.sheaf.test", "Ellie", "Park", ChurchMembershipRole.Leader, "Leads sophomore co-ed group"),
        new("ruth@demo.sheaf.test", "Ruth", "Daniels", ChurchMembershipRole.Staff, "Campus pastor (Staff)"),
        new("admin@demo.sheaf.test", "Alex", "Kim", ChurchMembershipRole.Admin, "Church admin"),
    ];

    public static readonly string[] Dorms = ["Smith Hall", "Alder Hall", "Birch Hall", "Cedar Commons", "Off campus"];
    public static readonly string[] HowWeMet = ["Welcome week BBQ", "Activities fair table", "Sunday service", "Smith Hall move-in", "Friend brought them"];
}

public sealed partial class DemoSeeder(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ITenantContextInitializer tenant,
    TimeProvider time,
    ILogger<DemoSeeder> logger)
{
    private sealed record GroupPlan(string Name, string[] LeaderEmails, DayOfWeek Day, bool? Male, string? Dorm, string? ClassYear, int Size);

    private static readonly string[] FirstSteps = ["Invite to small group this week", "Text about Sunday service", "Grab coffee after class", "Introduce to someone in their dorm"];

    private static readonly GroupPlan[] Groups =
    [
        new("Smith Hall Freshman Guys", ["jake@demo.sheaf.test", "devon@demo.sheaf.test"], DayOfWeek.Tuesday, true, "Smith Hall", "Freshman", 10),
        new("Thursday Women's Study", ["maria@demo.sheaf.test", "priya@demo.sheaf.test"], DayOfWeek.Thursday, false, null, null, 10),
        new("Grad & Young Pros", ["sam@demo.sheaf.test"], DayOfWeek.Wednesday, null, null, "Grad", 8),
        new("Sophomore Co-ed", ["ellie@demo.sheaf.test"], DayOfWeek.Monday, null, null, "Sophomore", 9),
    ];

    public async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (await db.Churches.AnyAsync(c => c.Slug == DemoData.ChurchSlug, ct))
        {
            return;
        }

        await SeedAsync(ct);
    }

    /// <summary>Deletes the demo church, its data and the demo logins, then seeds them again.</summary>
    public async Task ResetAsync(CancellationToken ct)
    {
        Guid? churchId = await db.Churches.Where(c => c.Slug == DemoData.ChurchSlug).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
        if (churchId is { } id)
        {
            SelectChurch(id);
            // Children before parents: every foreign key is Restrict.
            await db.PocAuditEntries.ExecuteDeleteAsync(ct);
            await db.PocProposals.ExecuteDeleteAsync(ct);
            await db.PocSensitiveNotes.ExecuteDeleteAsync(ct);
            await db.PocCaptures.ExecuteDeleteAsync(ct);
            await db.PocTouchpoints.ExecuteDeleteAsync(ct);
            await db.PocPersonProfiles.ExecuteDeleteAsync(ct);
            await db.Attendances.ExecuteDeleteAsync(ct);
            await db.GroupMeetings.ExecuteDeleteAsync(ct);
            await db.GroupAssignments.ExecuteDeleteAsync(ct);
            await db.Groups.ExecuteDeleteAsync(ct);
            await db.PersonDuplicateDismissals.ExecuteDeleteAsync(ct);
            await db.GroupTypes.ExecuteDeleteAsync(ct);
            await db.People.ExecuteDeleteAsync(ct);
            await db.Households.ExecuteDeleteAsync(ct);
            await db.IntegrationTenantLinks.Where(l => l.ChurchId == id).ExecuteDeleteAsync(ct);
            await db.ChurchIntegrations.Where(i => i.ChurchId == id).ExecuteDeleteAsync(ct);
            // Memberships go through SaveChanges so the security-stamp interceptor signs those users out.
            db.ChurchMemberships.RemoveRange(await db.ChurchMemberships.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            await db.Churches.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        }

        foreach (DemoAccount account in DemoData.Accounts)
        {
            if (await userManager.FindByEmailAsync(account.Email) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }

        db.ChangeTracker.Clear();
        await SeedAsync(ct);
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        Faker f = new("en") { Random = new Randomizer(2026) };
        // Touchpoints draw from their own sequence so adding them does not change the names and attendance above.
        Randomizer touch = new(7);
        DateTimeOffset now = time.GetUtcNow();
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(DemoData.TimeZoneId);
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        DateTimeOffset termStart = now.AddDays(-60);

        // Logins first: Identity lives outside the church.
        Dictionary<string, ApplicationUser> users = [];
        foreach (DemoAccount account in DemoData.Accounts)
        {
            ApplicationUser user = new() { UserName = account.Email, Email = account.Email, EmailConfirmed = true };
            IdentityResult result = await userManager.CreateAsync(user, DemoData.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create demo login {account.Email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            users[account.Email] = user;
        }

        Church church = new() { Slug = DemoData.ChurchSlug, Name = DemoData.ChurchName, TimeZoneId = DemoData.TimeZoneId };
        db.Churches.Add(church);
        await db.SaveChangesAsync(ct);
        SelectChurch(church.Id);

        // Memberships and the leaders' own Person records.
        Dictionary<string, Person> leaderPeople = [];
        foreach (DemoAccount account in DemoData.Accounts)
        {
            db.ChurchMemberships.Add(new ChurchMembership { UserId = users[account.Email].Id, Role = account.Role });
            Person person = new()
            {
                FirstName = account.FirstName,
                LastName = account.LastName,
                Email = account.Email,
                UserId = users[account.Email].Id,
                CreatedAt = termStart,
            };
            db.People.Add(person);
            db.PocPersonProfiles.Add(new PocPersonProfile
            {
                PersonId = person.Id,
                ClassYear = account.Role == ChurchMembershipRole.Leader ? "Senior" : null,
                Source = PersonSource.Seeded,
            });
            leaderPeople[account.Email] = person;
        }

        // Students. Phones use the fictional 555-01xx range, one unique number each.
        int[] areaCodes = [312, 773, 847, 630];
        string[] classYears = ["Freshman", "Freshman", "Sophomore", "Junior", "Senior", "Grad"];
        string[] leaderEmails = DemoData.Accounts.Where(a => a.Role == ChurchMembershipRole.Leader).Select(a => a.Email).ToArray();
        List<(Person Person, PocPersonProfile Profile, bool Male)> students = [];
        for (int i = 0; i < 120; i++)
        {
            bool male = f.Random.Bool();
            string first = f.Name.FirstName(male ? Bogus.DataSets.Name.Gender.Male : Bogus.DataSets.Name.Gender.Female);
            string last = f.Name.LastName();
            PersonSource source = i < 40 ? PersonSource.PlanningCenterSimulated : i < 95 ? PersonSource.Seeded : PersonSource.LeaderEntered;
            bool capturedThisWeek = source == PersonSource.LeaderEntered;
            string? capturedBy = capturedThisWeek ? users[f.PickRandom(leaderEmails)].Id : null;

            Person person = new()
            {
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}{i}@northgate.edu".ToLowerInvariant(),
                Phone = $"({areaCodes[i / 100]}) 555-01{i % 100:00}",
                CreatedAt = capturedThisWeek ? now.AddHours(-f.Random.Int(6, 200)) : termStart.AddDays(-f.Random.Int(30, 700)),
                CreatedByUserId = capturedBy,
            };
            PocPersonProfile profile = new()
            {
                PersonId = person.Id,
                Dorm = f.PickRandom(DemoData.Dorms),
                ClassYear = f.PickRandom(classYears),
                HowWeMet = capturedThisWeek ? f.PickRandom(DemoData.HowWeMet) : null,
                Source = source,
            };
            db.People.Add(person);
            db.PocPersonProfiles.Add(profile);
            students.Add((person, profile, male));
            if (capturedBy is not null)
            {
                db.PocAuditEntries.Add(new PocAuditEntry { At = person.CreatedAt, ActorUserId = capturedBy, Action = "person.added", Summary = $"Added {first} {last} with quick add", PersonId = person.Id });
                db.PocTouchpoints.Add(new PocTouchpoint { PersonId = person.Id, UserId = capturedBy, At = person.CreatedAt, Kind = TouchpointKind.Met, Note = profile.HowWeMet });
                // About half have had a first text; the rest show up as "reach out within 48 hours" or overdue.
                if (touch.Bool())
                {
                    DateTimeOffset texted = person.CreatedAt.AddHours(touch.Int(3, 30));
                    if (texted < now)
                    {
                        db.PocTouchpoints.Add(new PocTouchpoint { PersonId = person.Id, UserId = capturedBy, At = texted, Kind = TouchpointKind.Texted });
                    }
                }
                else
                {
                    profile.NextStep = touch.ArrayElement(FirstSteps);
                }
            }
        }

        // A planted duplicate for the "same person?" moment: the leader-entered Chris shares a phone with Christopher from Planning Center.
        Person christopher = students.First(s => s.Male && s.Profile.Source == PersonSource.PlanningCenterSimulated).Person;
        christopher.FirstName = "Christopher";
        christopher.LastName = "Nguyen";
        christopher.Email = "christopher.nguyen@northgate.edu";
        Person chris = new()
        {
            FirstName = "Chris",
            LastName = "Nguyen",
            Phone = christopher.Phone,
            CreatedAt = now.AddHours(-30),
            CreatedByUserId = users["jake@demo.sheaf.test"].Id,
        };
        db.People.Add(chris);
        db.PocPersonProfiles.Add(new PocPersonProfile
        {
            PersonId = chris.Id,
            Dorm = "Smith Hall",
            ClassYear = "Freshman",
            HowWeMet = "Smith Hall move-in",
            Source = PersonSource.LeaderEntered,
        });
        string jake = users["jake@demo.sheaf.test"].Id;
        PocCapture chrisCapture = new()
        {
            CreatedByUserId = jake,
            CreatedAt = chris.CreatedAt,
            Kind = CaptureKind.QuickAdd,
            Status = CaptureStatus.Ready,
            Text = "Added Chris Nguyen (Smith Hall move-in)",
        };
        db.PocCaptures.Add(chrisCapture);
        db.PocTouchpoints.Add(new PocTouchpoint { PersonId = chris.Id, UserId = jake, At = chris.CreatedAt, Kind = TouchpointKind.Met, Note = "Smith Hall move-in" });
        db.PocAuditEntries.Add(new PocAuditEntry { At = chris.CreatedAt, ActorUserId = jake, Action = "person.added", Summary = "Added Chris Nguyen with quick add", PersonId = chris.Id });
        db.PocProposals.Add(new PocProposal
        {
            CaptureId = chrisCapture.Id,
            OwnerUserId = jake,
            CreatedAt = chris.CreatedAt,
            Kind = ProposalKind.MergeDuplicates,
            Title = "Is Chris Nguyen the same person as Christopher Nguyen?",
            Payload = $$"""{"firstName":"Chris","lastName":"Nguyen","phone":"{{chris.Phone}}","email":null,"dorm":"Smith Hall","classYear":"Freshman","howWeMet":"Smith Hall move-in","isMinor":false,"nextStep":null,"notes":null}""",
            Reasons = "Same phone number\nSame last name, and \"Chris\" can be short for \"Christopher\"",
            TargetPersonId = christopher.Id,
            DuplicatePersonId = chris.Id,
        });

        // The Easter guest family: a parent and a minor. The minor has no phone and contact goes through the parent.
        Household brooks = new() { Label = "The Brooks Family" };
        db.Households.Add(brooks);
        Person dana = new()
        {
            FirstName = "Dana",
            LastName = "Brooks",
            Phone = "(630) 555-0177",
            Email = "dana.brooks@example.com",
            HouseholdId = brooks.Id,
            CreatedAt = now.AddDays(-12),
            CreatedByUserId = users["ruth@demo.sheaf.test"].Id,
        };
        Person lily = new()
        {
            FirstName = "Lily",
            LastName = "Brooks",
            HouseholdId = brooks.Id,
            CreatedAt = now.AddDays(-12),
            CreatedByUserId = users["ruth@demo.sheaf.test"].Id,
        };
        db.People.AddRange(dana, lily);
        db.PocPersonProfiles.Add(new PocPersonProfile { PersonId = dana.Id, HowWeMet = "Easter service", Source = PersonSource.LeaderEntered });
        db.PocPersonProfiles.Add(new PocPersonProfile
        {
            PersonId = lily.Id,
            HowWeMet = "Easter service",
            Source = PersonSource.LeaderEntered,
            IsMinor = true,
            GuardianPersonId = dana.Id,
        });

        // Groups, rosters and six weeks of attendance with a few drifting members and visitors.
        GroupType smallGroup = new() { Name = "Small group" };
        db.GroupTypes.Add(smallGroup);
        HashSet<Guid> placed = [];
        int groupIndex = 0;
        foreach (GroupPlan plan in Groups)
        {
            Group group = new() { Name = plan.Name, StartsAt = termStart, GroupTypeId = smallGroup.Id };
            db.Groups.Add(group);
            foreach (string email in plan.LeaderEmails)
            {
                db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = leaderPeople[email].Id, Role = GroupRole.Leader, StartsAt = termStart });
            }

            List<Person> members = students
                .Where(s => s.Profile.Source != PersonSource.LeaderEntered && !placed.Contains(s.Person.Id))
                .Where(s => plan.Male is null || s.Male == plan.Male)
                .Where(s => plan.ClassYear is null || s.Profile.ClassYear == plan.ClassYear || plan.Dorm is not null)
                .Take(plan.Size)
                .Select(s => s.Person)
                .ToList();
            foreach ((Person member, int index) in members.Select((m, i) => (m, i)))
            {
                placed.Add(member.Id);
                db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = member.Id, Role = GroupRole.Member, StartsAt = termStart });
                if (plan.Dorm is not null)
                {
                    students.Single(s => s.Person.Id == member.Id).Profile.Dorm = plan.Dorm;
                }
            }

            // The last two members drift: they came early on and have missed the last two meetings.
            HashSet<Guid> drifting = members.TakeLast(2).Select(m => m.Id).ToHashSet();
            Dictionary<Guid, double> reliability = members.ToDictionary(m => m.Id, _ => f.Random.Double(0.65, 0.95));
            string recorder = users[plan.LeaderEmails[0]].Id;

            // Leaders keep up with most of their group; drifting members and a few others have gone quiet.
            foreach (Person member in members)
            {
                string leader = users[touch.ArrayElement(plan.LeaderEmails)].Id;
                (int From, int To) daysAgo = drifting.Contains(member.Id) ? (18, 26) : touch.Double() < 0.25 ? (15, 30) : (1, 12);
                int count = drifting.Contains(member.Id) ? 1 : touch.Int(1, 2);
                for (int t = 0; t < count; t++)
                {
                    TouchpointKind kind = touch.ArrayElement(new[] { TouchpointKind.Texted, TouchpointKind.Texted, TouchpointKind.Coffee, TouchpointKind.SawThem, TouchpointKind.Called });
                    db.PocTouchpoints.Add(new PocTouchpoint { PersonId = member.Id, UserId = leader, At = now.AddDays(-touch.Int(daysAgo.From, daysAgo.To)).AddHours(-touch.Int(0, 10)), Kind = kind });
                }
            }

            DateOnly lastMeeting = today.AddDays(-1);
            while (lastMeeting.DayOfWeek != plan.Day)
            {
                lastMeeting = lastMeeting.AddDays(-1);
            }

            // A returning visitor in two of the last three weeks (worth inviting to join) and a one-time visitor earlier.
            List<Person> unplaced = [.. students
                .Where(s => !placed.Contains(s.Person.Id) && s.Profile.Source != PersonSource.LeaderEntered)
                .Where(s => plan.Male is null || s.Male == plan.Male)
                .Select(s => s.Person)];
            Person returning = f.PickRandom(unplaced);
            Person oneTime = f.PickRandom(unplaced.Where(p => p.Id != returning.Id));
            placed.Add(returning.Id);
            placed.Add(oneTime.Id);

            for (int week = 5; week >= 0; week--)
            {
                DateOnly date = lastMeeting.AddDays(-7 * week);
                DateTime localEvening = date.ToDateTime(new TimeOnly(21, 0));
                DateTimeOffset recordedAt = new(localEvening, zone.GetUtcOffset(localEvening));
                GroupMeeting session = new() { GroupId = group.Id, OccurredOn = date, RecordedAt = recordedAt, RecordedByUserId = recorder };
                db.GroupMeetings.Add(session);

                // Leaders are on the roster too, so they are marked present at their own meetings.
                foreach (string email in plan.LeaderEmails)
                {
                    db.Attendances.Add(new Attendance { MeetingId = session.Id, PersonId = leaderPeople[email].Id, RecordedAt = recordedAt, RecordedByUserId = recorder });
                }

                foreach (Person member in members)
                {
                    // The sophomore group has thinned out over the last month; the women's study has picked up.
                    double trend = plan.Name.StartsWith("Sophomore", StringComparison.Ordinal) ? (week <= 3 ? 0.6 : 1.0)
                        : plan.Name.StartsWith("Thursday", StringComparison.Ordinal) ? (week <= 3 ? 1.1 : 0.7) : 1.0;
                    bool came = drifting.Contains(member.Id) ? week >= 2 : f.Random.Double() < reliability[member.Id] * trend;
                    if (came)
                    {
                        db.Attendances.Add(new Attendance { MeetingId = session.Id, PersonId = member.Id, RecordedAt = recordedAt, RecordedByUserId = recorder });
                    }
                }

                Person? visitor = week switch { 0 or 2 => returning, 3 => oneTime, _ => null };
                if (visitor is not null)
                {
                    db.Attendances.Add(new Attendance { MeetingId = session.Id, PersonId = visitor.Id, RecordedAt = recordedAt, RecordedByUserId = recorder });
                }

                // This week's captures carry through the funnel: someone met earlier in the week came to the last
                // meeting after a text, and every other group also gained them as a new member.
                if (week == 0)
                {
                    var newcomer = students.FirstOrDefault(s => s.Profile.Source == PersonSource.LeaderEntered && !placed.Contains(s.Person.Id)
                        && s.Person.CreatedAt < recordedAt.AddDays(-1) && (plan.Male is null || s.Male == plan.Male) && s.Person.CreatedByUserId is not null);
                    if (newcomer.Person is { } p && p.CreatedByUserId is { } addedBy)
                    {
                        placed.Add(p.Id);
                        db.Attendances.Add(new Attendance { MeetingId = session.Id, PersonId = p.Id, RecordedAt = recordedAt, RecordedByUserId = recorder });
                        if (!db.PocTouchpoints.Local.Any(t => t.PersonId == p.Id && t.Kind == TouchpointKind.Texted))
                        {
                            db.PocTouchpoints.Add(new PocTouchpoint { PersonId = p.Id, UserId = addedBy, At = p.CreatedAt.AddHours(6), Kind = TouchpointKind.Texted });
                        }

                        if (groupIndex % 2 == 0)
                        {
                            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = p.Id, Role = GroupRole.Member, StartsAt = recordedAt });
                        }
                    }
                }
            }

            groupIndex++;
        }

        await db.SaveChangesAsync(ct);
        LogSeeded(logger, church.Id);
    }

    /// <summary>
    /// POC exemption: the seeder runs only in Development, in its own DI scope with no signed-in user,
    /// and writes only the demo church it creates.
    /// </summary>
    private void SelectChurch(Guid churchId)
    {
#pragma warning disable RS0030
        tenant.Initialize(churchId, null);
#pragma warning restore RS0030
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the demo church {ChurchId}.")]
    private static partial void LogSeeded(ILogger logger, Guid churchId);
}
