namespace SelfHealing.Core;

/// <summary>
/// Scores a piece of text against a field's synonyms.
/// An exact match ("First Name") is 1. A longer caption that only contains the
/// synonym ("Emergency First Name") is <see cref="PartialStrength"/>, so a more
/// specific decoy cannot tie the plain field.
/// </summary>
public static class TextMatcher
{
    public const double PartialStrength = 0.55;

    public static double Strength(string? text, IReadOnlyList<string> synonyms)
    {
        var normalized = TextNormalizer.Normalize(text);
        if (normalized.Length == 0 || synonyms.Count == 0)
        {
            return 0;
        }

        var best = 0d;
        foreach (var synonym in synonyms)
        {
            var needle = TextNormalizer.Normalize(synonym);
            if (needle.Length == 0)
            {
                continue;
            }

            if (normalized == needle)
            {
                return 1;
            }

            if (TextNormalizer.ContainsPhrase(normalized, needle))
            {
                best = Math.Max(best, PartialStrength);
            }
        }

        return best;
    }

    public static bool HitsPenalty(string? text, IReadOnlyList<string> penalties)
    {
        var normalized = TextNormalizer.Normalize(text);
        var fromAttribute = TextNormalizer.NormalizeAttribute(text);
        return MatchesAny(normalized, penalties) || MatchesAny(fromAttribute, penalties);
    }

    private static bool MatchesAny(string normalized, IReadOnlyList<string> penalties)
    {
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var penalty in penalties)
        {
            var needle = TextNormalizer.Normalize(penalty);
            if (needle.Length == 0)
            {
                continue;
            }

            if (normalized == needle || TextNormalizer.ContainsPhrase(normalized, needle))
            {
                return true;
            }
        }

        return false;
    }
}
