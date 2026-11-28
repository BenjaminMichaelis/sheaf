using Sheaf.Data;
using Sheaf.Data.Poc;

namespace Sheaf.Poc.Capture;

/// <summary>An existing person who may be the same as someone new, with the reasons why.</summary>
public sealed record MatchCandidate(Person Person, PocPersonProfile? Profile, int Score, string[] Reasons);

/// <summary>
/// Duplicate check in order of confidence: exact phone, exact email, then a similar name with context (same last
/// name and a matching or shortened first name, or the same first name and dorm).
/// </summary>
public static class PersonMatcher
{
    private static readonly string[][] Nicknames =
    [
        ["chris", "christopher", "christian", "christina", "christine"],
        ["mike", "michael"], ["matt", "matthew"], ["alex", "alexander", "alexandra"], ["sam", "samuel", "samantha"],
        ["ben", "benjamin"], ["kate", "katie", "katherine", "kathryn"], ["will", "william", "liam"], ["nick", "nicholas"],
        ["liz", "elizabeth", "beth"], ["jen", "jenny", "jennifer"], ["dan", "danny", "daniel"], ["tom", "thomas"],
        ["josh", "joshua"], ["abby", "abigail"], ["tony", "anthony"], ["jake", "jacob"], ["zach", "zachary"],
    ];

    public static MatchCandidate? Best(ProposedPerson incoming, IEnumerable<(Person Person, PocPersonProfile? Profile)> existing) =>
        existing.Select(e => Score(incoming, e.Person, e.Profile)).OfType<MatchCandidate>()
            .OrderByDescending(m => m.Score).FirstOrDefault();

    private static MatchCandidate? Score(ProposedPerson incoming, Person person, PocPersonProfile? profile)
    {
        List<string> reasons = [];
        int score = 0;

        if (Digits(incoming.Phone) is { Length: >= 10 } phone && Digits(person.Phone) == phone)
        {
            reasons.Add("Same phone number");
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Email) && string.Equals(incoming.Email.Trim(), person.Email, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Same email address");
            score += 100;
        }

        string first = incoming.FirstName.Trim().ToLowerInvariant();
        string otherFirst = person.FirstName.ToLowerInvariant();
        bool sameLast = !string.IsNullOrWhiteSpace(incoming.LastName)
            && string.Equals(incoming.LastName.Trim(), person.LastName, StringComparison.OrdinalIgnoreCase);
        bool sameFirst = first == otherFirst;
        bool nickname = !sameFirst && (otherFirst.StartsWith(first, StringComparison.Ordinal) || first.StartsWith(otherFirst, StringComparison.Ordinal)
            || Nicknames.Any(set => set.Contains(first) && set.Contains(otherFirst)));

        if (sameLast && (sameFirst || nickname))
        {
            reasons.Add(sameFirst ? "Same name" : $"Same last name, and \"{incoming.FirstName}\" can be short for \"{person.FirstName}\"");
            score += sameFirst ? 60 : 50;
        }
        else if (sameFirst && incoming.Dorm is not null && string.Equals(incoming.Dorm, profile?.Dorm, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"Same first name, also in {profile?.Dorm}");
            score += 30;
        }

        return score >= 30 ? new MatchCandidate(person, profile, score, [.. reasons]) : null;
    }

    private static string? Digits(string? phone)
    {
        if (phone is null)
        {
            return null;
        }

        string digits = new([.. phone.Where(char.IsDigit)]);
        return digits.Length > 10 ? digits[^10..] : digits;
    }
}
