namespace SelfHealing.Core;

/// <summary>
/// Makes label text, option text, and attribute values comparable.
/// The practice form pads several of these (" State ", " Afganistan ").
/// </summary>
public static partial class TextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var collapsed = Whitespace().Replace(text.Replace('\u00A0', ' ').Trim(), " ");
        return collapsed.ToLowerInvariant();
    }

    /// <summary>
    /// Turns an id or name into words. "first_name", "first-name" and "FirstName"
    /// all become "first name". This is the whole of the "fuzzy" id/name match:
    /// edit distance is not used, because a one-character gap turns "fname" into "lname".
    /// </summary>
    public static string NormalizeAttribute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var spaced = CamelBoundary().Replace(value, "$1 $2");
        spaced = NotAlphanumeric().Replace(spaced, " ");
        return Normalize(spaced);
    }

    /// <summary>
    /// True when <paramref name="phrase"/> occurs in <paramref name="haystack"/>
    /// on a word boundary. "state" matches "home state" and does not match "statement"
    /// or "united states".
    /// </summary>
    public static bool ContainsPhrase(string haystack, string phrase)
    {
        if (phrase.Length == 0 || haystack.Length == 0)
        {
            return false;
        }

        var start = 0;
        while (start <= haystack.Length - phrase.Length)
        {
            var index = haystack.IndexOf(phrase, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var beforeOk = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var end = index + phrase.Length;
            var afterOk = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NotAlphanumeric();
}
