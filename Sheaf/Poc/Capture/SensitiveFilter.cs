using System.Text.RegularExpressions;

namespace Sheaf.Poc.Capture;

/// <summary>The note with sensitive sentences replaced, and which categories matched.</summary>
public sealed record RedactionResult(string Text, string[] Categories)
{
    public bool Redacted => Categories.Length > 0;
}

/// <summary>
/// Keyword filter that runs before any AI call. A sentence that mentions something pastoral or legally sensitive is
/// replaced, so Claude never sees it; the original goes to the Staff-only pastor inbox. Deliberately over-eager: a false
/// positive costs a pastor a glance, a false negative sends someone's crisis to a model.
/// </summary>
public static partial class SensitiveFilter
{
    public const string Placeholder = "[sensitive detail sent to a pastor]";

    private static readonly (string Category, Regex Pattern)[] Rules =
    [
        ("health", Health()),
        ("mental health", MentalHealth()),
        ("abuse or safety", Abuse()),
        ("addiction", Addiction()),
        ("legal or immigration", Legal()),
        ("grief or family crisis", Grief()),
        ("sexuality", Sexuality()),
        ("housing or money", Money()),
    ];

    public static RedactionResult Redact(string text)
    {
        HashSet<string> categories = [];
        string[] sentences = SentenceSplit().Split(text);
        for (int i = 0; i < sentences.Length; i++)
        {
            string[] hits = Rules.Where(r => r.Pattern.IsMatch(sentences[i])).Select(r => r.Category).ToArray();
            if (hits.Length > 0)
            {
                categories.UnionWith(hits);
                sentences[i] = Placeholder;
            }
        }

        return new RedactionResult(string.Join(' ', sentences).Trim(), [.. Rules.Select(r => r.Category).Where(categories.Contains)]);
    }

    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\b(cancer|chemo\w*|tumou?r|diagnos\w*|hospital\w*|surgery|pregnan\w*|miscarr\w*|seizures?|chronic (illness|pain))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Health();

    [GeneratedRegex(@"\b(suicid\w*|self[- ]harm\w*|cutting|depress\w*|panic attacks?|anxiety|eating disorder|anorexi\w*|bulimi\w*|therap(y|ist)|counsel(ing|or)|psychiatr\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MentalHealth();

    [GeneratedRegex(@"\b(abus\w*|assault\w*|rape\w*|molest\w*|domestic violence|unsafe at home|hits? (him|her|them))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Abuse();

    [GeneratedRegex(@"\b(addict\w*|relapse\w*|rehab|overdos\w*|drunk|sober\w*|AA meetings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Addiction();

    [GeneratedRegex(@"\b(arrest\w*|jail|probation|DUI|undocumented|deport\w*|immigration status)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Legal();

    [GeneratedRegex(@"\b(divorc\w*|passed away|funeral|died|grie(f|ving))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Grief();

    [GeneratedRegex(@"\b(sexual\w*|came out|coming out|gay|lesbian|bisexual|transgender)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sexuality();

    [GeneratedRegex(@"\b(evict\w*|homeless\w*|can'?t afford|food insecur\w*|in debt)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Money();
}
