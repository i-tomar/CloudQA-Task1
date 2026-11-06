namespace SelfHealing.Core;

/// <summary>
/// Adds the signals up. Heaviest first: linked label, accessible name, nearby
/// text, placeholder, then id or name. A last-name penalty is subtracted once.
/// Adding them up is deliberate. The practice form has a misleading for= on a
/// span; a strict "trust the first signal" chain would follow it. Several
/// signals that agree outvote one that is wrong, and the margin rule refuses
/// the match when they do not agree by enough.
/// </summary>
public static class FieldScorer
{
    public static ScoredCandidate Score(
        IWebElement element,
        int index,
        FieldSpec spec,
        IWebElement form,
        SignalWeights weights)
    {
        var candidate = new ScoredCandidate
        {
            Element = element,
            Index = index,
            Description = DomHelpers.Describe(element),
        };

        var total = 0;
        var linked = DomHelpers.LinkedLabels(element, form);
        var nearby = DomHelpers.Nearby(element);
        var labelSources = linked.Concat(nearby.Where(source => source.Kind == SignalNames.Label)).ToList();
        var nearbySources = nearby.Where(source => source.Kind == SignalNames.NearbyText).ToList();
        var accessible = DomHelpers.AccessibleNames(element, form)
            .Concat(nearby.Where(source => source.Kind == SignalNames.AccessibleName))
            .ToList();

        total += AddBest(candidate, SignalNames.Label, weights.Label, labelSources, spec);
        total += AddBest(candidate, SignalNames.AccessibleName, weights.AccessibleName, accessible, spec);
        total += AddBest(candidate, SignalNames.NearbyText, weights.NearbyText, nearbySources, spec);
        total += AddPlaceholder(candidate, element, spec, weights);
        total += AddIdOrName(candidate, element, spec, weights);
        total += AddPenalty(candidate, element, spec, weights, labelSources, nearbySources, accessible);

        candidate.Score = total;
        return candidate;
    }

    /// <summary>
    /// A radio group is scored from its heading (label, legend, or nearby text)
    /// and, weakly, from the shared name attribute. The option captions are not
    /// the heading.
    /// </summary>
    public static ScoredCandidate ScoreGroup(
        IReadOnlyList<IWebElement> radios,
        int index,
        string groupName,
        FieldSpec spec,
        SignalWeights weights)
    {
        var candidate = new ScoredCandidate
        {
            Element = radios[0],
            Index = index,
            GroupName = groupName,
            Description = string.IsNullOrEmpty(groupName)
                ? $"radio group ({radios.Count} options, no shared name)"
                : $"radio group name={groupName} ({radios.Count} options)",
        };
        candidate.Radios.AddRange(radios);

        var sources = new List<TextSource>();
        foreach (var radio in radios)
        {
            sources.AddRange(DomHelpers.Nearby(radio));
            sources.AddRange(DomHelpers.AccessibleNames(radio, radio));
        }

        var total = 0;
        total += AddBest(candidate, SignalNames.Label, weights.Label, sources.Where(s => s.Kind == SignalNames.Label).ToList(), spec);
        total += AddBest(candidate, SignalNames.AccessibleName, weights.AccessibleName, sources.Where(s => s.Kind == SignalNames.AccessibleName).ToList(), spec);
        total += AddBest(candidate, SignalNames.NearbyText, weights.NearbyText, sources.Where(s => s.Kind == SignalNames.NearbyText).ToList(), spec);

        var namePoints = DomHelpers.Scale(weights.IdOrName, TextMatcher.Strength(TextNormalizer.NormalizeAttribute(groupName), spec.Synonyms));
        if (namePoints > 0)
        {
            candidate.Signals.Add(new SignalContribution
            {
                Name = SignalNames.IdOrName,
                Points = namePoints,
                Evidence = "name=" + groupName,
            });
            total += namePoints;
        }

        candidate.Score = total;
        return candidate;
    }

    private static int AddBest(
        ScoredCandidate candidate,
        string signalName,
        int weight,
        IReadOnlyList<TextSource> sources,
        FieldSpec spec)
    {
        var bestPoints = 0;
        string? evidence = null;
        foreach (var source in sources)
        {
            var points = DomHelpers.Scale(weight, TextMatcher.Strength(source.Text, spec.Synonyms));
            if (points > bestPoints)
            {
                bestPoints = points;
                evidence = source.Text;
            }
        }

        if (bestPoints == 0)
        {
            return 0;
        }

        candidate.Signals.Add(new SignalContribution
        {
            Name = signalName,
            Points = bestPoints,
            Evidence = evidence,
        });
        return bestPoints;
    }

    private static int AddPlaceholder(
        ScoredCandidate candidate,
        IWebElement element,
        FieldSpec spec,
        SignalWeights weights)
    {
        var placeholder = element.GetAttribute("placeholder");
        if (string.IsNullOrWhiteSpace(placeholder) || TextMatcher.HitsPenalty(placeholder, spec.PenaltyTerms))
        {
            return 0;
        }

        var points = DomHelpers.Scale(weights.Placeholder, TextMatcher.Strength(placeholder, spec.Synonyms));
        if (points == 0)
        {
            var normalized = TextNormalizer.Normalize(placeholder);
            var hinted = spec.PlaceholderHints.Any(hint => TextNormalizer.Normalize(hint) == normalized);
            if (hinted)
            {
                points = weights.Placeholder;
            }
        }

        if (points == 0)
        {
            return 0;
        }

        candidate.Signals.Add(new SignalContribution
        {
            Name = SignalNames.Placeholder,
            Points = points,
            Evidence = placeholder.Trim(),
        });
        return points;
    }

    private static int AddIdOrName(
        ScoredCandidate candidate,
        IWebElement element,
        FieldSpec spec,
        SignalWeights weights)
    {
        var id = element.GetAttribute("id");
        var name = element.GetAttribute("name");
        var idPoints = DomHelpers.Scale(weights.IdOrName, TextMatcher.Strength(TextNormalizer.NormalizeAttribute(id), spec.Synonyms));
        var namePoints = DomHelpers.Scale(weights.IdOrName, TextMatcher.Strength(TextNormalizer.NormalizeAttribute(name), spec.Synonyms));
        var best = Math.Max(idPoints, namePoints);
        if (best == 0)
        {
            return 0;
        }

        string evidence;
        if (idPoints == best && namePoints == best)
        {
            evidence = $"id={id}; name={name}";
        }
        else if (idPoints == best)
        {
            evidence = "id=" + id;
        }
        else
        {
            evidence = "name=" + name;
        }

        candidate.Signals.Add(new SignalContribution
        {
            Name = SignalNames.IdOrName,
            Points = best,
            Evidence = evidence,
        });
        return best;
    }

    private static int AddPenalty(
        ScoredCandidate candidate,
        IWebElement element,
        FieldSpec spec,
        SignalWeights weights,
        IReadOnlyList<TextSource> labels,
        IReadOnlyList<TextSource> nearby,
        IReadOnlyList<TextSource> accessible)
    {
        if (spec.PenaltyTerms.Count == 0 || weights.Penalty == 0)
        {
            return 0;
        }

        // Visible captions first, so the report quotes "Last Name" rather than the id.
        var samples = new List<string?>();
        samples.AddRange(labels.Select(source => source.Text));
        samples.AddRange(nearby.Select(source => source.Text));
        samples.AddRange(accessible.Select(source => source.Text));
        samples.Add(element.GetAttribute("placeholder"));
        samples.Add(element.GetAttribute("aria-label"));
        samples.Add(element.GetAttribute("id"));
        samples.Add(element.GetAttribute("name"));

        var evidence = samples.FirstOrDefault(sample => TextMatcher.HitsPenalty(sample, spec.PenaltyTerms));
        if (evidence == null)
        {
            return 0;
        }

        candidate.Signals.Add(new SignalContribution
        {
            Name = SignalNames.Penalty,
            Points = -weights.Penalty,
            Evidence = evidence,
        });
        return -weights.Penalty;
    }
}
