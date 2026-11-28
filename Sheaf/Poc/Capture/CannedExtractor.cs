using System.Text.RegularExpressions;

using Sheaf.Poc.Demo;

namespace Sheaf.Poc.Capture;

/// <summary>
/// A rough rule-based stand-in for Claude, used when no API key is set so the demo still flows. It finds names after
/// phrases like "met" or "talked to", and pulls phones, dorms and class years from the same stretch of text.
/// </summary>
public static partial class CannedExtractor
{
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "him", "her", "them", "some", "this", "that", "his", "my", "our", "at", "in", "on", "today", "tonight",
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Smith", "Alder", "Birch", "Cedar",
    };

    public static Extraction Extract(string text)
    {
        List<(int Index, string First, string? Last)> names = [];
        foreach (Match match in NameCue().Matches(text))
        {
            AddName(names, match.Groups["first"], match.Groups["last"]);
            foreach (Match more in AndName().Matches(text[(match.Index + match.Length)..]))
            {
                if (more.Index > 2)
                {
                    break;
                }

                AddName(names, more.Groups["first"], more.Groups["last"], match.Index + match.Length);
            }
        }

        names.Sort((a, b) => a.Index.CompareTo(b.Index));
        List<ExtractedPerson> people = [];
        for (int i = 0; i < names.Count; i++)
        {
            (int start, string first, string? last) = names[i];
            int end = i + 1 < names.Count ? names[i + 1].Index : text.Length;
            string segment = text[start..end];
            string sentence = SentenceAround(text, start);

            string? phone = Phone().Match(segment) is { Success: true } p ? p.Value : null;
            string? dorm = DemoData.Dorms.FirstOrDefault(d => segment.Contains(d, StringComparison.OrdinalIgnoreCase));
            string? classYear = ClassYear().Match(segment) is { Success: true } y ? Capitalize(y.Value) : null;
            bool minor = Minor().IsMatch(segment);
            string? nextStep = FollowUp().Match(segment) is { Success: true } f ? Capitalize(f.Value.TrimEnd('.', ' ')) : $"Text {first} to say it was great to meet";

            string? howWeMet = Place().Match(sentence) is { Success: true } place ? Capitalize(place.Groups["place"].Value.Trim()) : null;

            people.Add(new ExtractedPerson(first, last, minor ? null : phone, null, dorm, classYear, howWeMet, minor, minor ? null : nextStep, null, sentence));
        }

        string summary = people.Count switch
        {
            0 => "No new people found in this note.",
            1 => $"Found {people[0].FirstName} in your note.",
            _ => $"Found {people.Count} people in your note.",
        };
        return new Extraction([.. people], summary);
    }

    private static void AddName(List<(int, string, string?)> names, Group first, Group last, int offset = 0)
    {
        if (!first.Success || NotNames.Contains(first.Value) || names.Exists(n => n.Item2 == first.Value))
        {
            return;
        }

        string? lastName = last.Success && !NotNames.Contains(last.Value) ? last.Value : null;
        names.Add((first.Index + offset, first.Value, lastName));
    }

    private static string SentenceAround(string text, int index)
    {
        int start = text.LastIndexOfAny(['.', '!', '?', '\n'], Math.Max(0, index - 1)) + 1;
        int end = text.IndexOfAny(['.', '!', '?', '\n'], index);
        return text[start..(end < 0 ? text.Length : end + 1)].Trim();
    }

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    [GeneratedRegex(@"\b(?i:met|meet|talked (?:to|with)|chatted with|named|name is|introduced to|sat with|hung out with)\s+(?<first>[A-Z][a-z]+)(?:\s+(?<last>[A-Z][a-z']+))?")]
    private static partial Regex NameCue();

    [GeneratedRegex(@"^\s*(?:,|and|&)\s+(?:also\s+)?(?<first>[A-Z][a-z]+)(?:\s+(?<last>[A-Z][a-z']+))?")]
    private static partial Regex AndName();

    [GeneratedRegex(@"\b(?:at|after|during)\s+(?:the\s+)?(?<place>[^.,!?\n]{3,60}?)(?=[.,!?\n]|\s+(?:and|but|so)\b|$)")]
    private static partial Regex Place();

    [GeneratedRegex(@"\(?\b\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}\b")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\b(freshman|sophomore|junior|senior|grad)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClassYear();

    [GeneratedRegex(@"\b(high school|1[3-7] ?(years? old|yo)|middle school|(his|her|their) (mom|dad|parents?))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Minor();

    [GeneratedRegex(@"\b(invite|text|follow up|grab coffee|coffee|give (him|her|them) a ride|pick (him|her|them) up)\b[^.!?\n]*", RegexOptions.IgnoreCase)]
    private static partial Regex FollowUp();
}
