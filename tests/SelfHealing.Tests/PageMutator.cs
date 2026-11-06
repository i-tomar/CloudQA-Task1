using System.Text.RegularExpressions;

namespace SelfHealing.Tests;

/// <summary>
/// Rewrites a fixture so the ids, names, classes and field order change,
/// while the visible captions, option values and the Transgender for= trap stay
/// meaningful. The trap is kept on purpose: the span's for is remapped with the
/// Female radio's id, so it still points at the wrong option.
/// </summary>
public static partial class PageMutator
{
    public static string Mutate(string html, int seed)
    {
        var reordered = ReorderFields(html, seed);
        var withIds = RewritePairedAttributes(reordered, "id", "for", seed, "f");
        var withNames = RewriteAttribute(withIds, "name", seed, "n");
        return RewriteClasses(withNames, seed);
    }

    public static string BlockSignature(string html)
    {
        var matches = FieldBlocks().Matches(html);
        return string.Join("|", matches.Select(match => match.Value));
    }

    private static string ReorderFields(string html, int seed)
    {
        var matches = FieldBlocks().Matches(html);
        if (matches.Count < 2)
        {
            return html;
        }

        var blocks = matches.Select(match => match.Value).ToList();
        var shuffled = blocks.ToList();
        var random = new Random(seed);
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        // A shuffle can land back on the original order. Swap the first two
        // blocks so every seed really does move the fields.
        if (shuffled.SequenceEqual(blocks))
        {
            (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
        }

        var index = 0;
        return FieldBlocks().Replace(html, _ => shuffled[index++]);
    }

    private static string RewritePairedAttributes(string html, string primary, string secondary, int seed, string prefix)
    {
        var values = AttributeValues(html, primary).Distinct().ToList();
        var map = values.Select((value, index) => (value, updated: $"{prefix}{seed}x{index}"))
            .ToDictionary(pair => pair.value, pair => pair.updated, StringComparer.Ordinal);
        html = ReplaceAttribute(html, primary, map);
        return ReplaceAttribute(html, secondary, map);
    }

    private static string RewriteAttribute(string html, string attribute, int seed, string prefix)
    {
        var values = AttributeValues(html, attribute).Distinct().ToList();
        var map = values.Select((value, index) => (value, updated: $"{prefix}{seed}x{index}"))
            .ToDictionary(pair => pair.value, pair => pair.updated, StringComparer.Ordinal);
        return ReplaceAttribute(html, attribute, map);
    }

    private static string RewriteClasses(string html, int seed)
    {
        var count = 0;
        return ClassAttribute().Replace(html, _ => $"class=\"c{seed}x{count++}\"");
    }

    private static IEnumerable<string> AttributeValues(string html, string attribute)
    {
        foreach (Match match in Attribute(attribute).Matches(html))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static string ReplaceAttribute(string html, string attribute, IReadOnlyDictionary<string, string> map)
    {
        return Attribute(attribute).Replace(html, match =>
        {
            var current = match.Groups[1].Value;
            var updated = map.TryGetValue(current, out var mapped) ? mapped : current;
            return $"{attribute}=\"{updated}\"";
        });
    }

    [GeneratedRegex("<!--field-->(.*?)<!--/field-->", RegexOptions.Singleline)]
    private static partial Regex FieldBlocks();

    [GeneratedRegex(@"\bclass\s*=\s*""[^""]*""")]
    private static partial Regex ClassAttribute();

    private static Regex Attribute(string name) => new($@"\b{name}\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
}
