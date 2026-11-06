namespace SelfHealing.Core;

/// <summary>
/// The result of resolving one field. The winner element is null unless the
/// status is <see cref="ResolutionStatus.Resolved"/>.
/// </summary>
public sealed class FieldResolution
{
    public required string Field { get; init; }

    public ResolutionStatus Status { get; init; }

    public string Reason { get; init; } = "";

    public IWebElement? Winner { get; init; }

    public ScoredOption? Option { get; init; }

    public int RejectedByHardFilter { get; init; }

    public IReadOnlyList<ScoredCandidate> Ranked { get; init; } = [];

    public IReadOnlyList<ScoredOption> Options { get; init; } = [];

    public string Summary => $"{AmbiguityRule.ToToken(Status)}: {Field} — {Reason}";

    public FieldReport ToReport()
    {
        var ranked = Ranked.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Index).ToList();
        var winnerRow = ranked.FirstOrDefault(candidate => candidate.IsWinner);

        var report = new FieldReport
        {
            Field = Field,
            Status = AmbiguityRule.ToToken(Status),
            Reason = Reason,
            RejectedByHardFilter = RejectedByHardFilter,
            Score = winnerRow?.Score ?? (ranked.Count > 0 ? ranked[0].Score : null),
            RunnerUpScore = ranked.Count > 1 ? ranked[1].Score : null,
            Matched = winnerRow?.Description,
            DecidingSignals = (winnerRow?.Signals ?? [])
                .OrderByDescending(signal => signal.Points)
                .Select(ToSignal)
                .ToList(),
            Candidates = ranked.Select(candidate => new CandidateReport
            {
                Index = candidate.Index,
                Score = candidate.Score,
                Description = candidate.Description,
                Winner = candidate.IsWinner,
                Signals = candidate.Signals.Select(ToSignal).ToList(),
            }).ToList(),
        };

        if (Option != null)
        {
            var how = Option.ValueMatched && Option.TextMatched
                ? "value and adjacent text"
                : Option.ValueMatched ? "value" : "adjacent text";
            report.Detail =
                $"option matched by {how}; value='{Option.Value}'; adjacent text='{Option.AdjacentText}'. Span for= attributes are not read.";
        }
        else if (Options.Count > 0)
        {
            report.Detail = "options considered: " + string.Join(
                "; ",
                Options.Select(option => $"{option.Description} score={option.Score}"));
        }

        return report;
    }

    private static SignalReport ToSignal(SignalContribution signal) => new()
    {
        Name = signal.Name,
        Points = signal.Points,
        Evidence = signal.Evidence,
    };
}

public static class ElementResolver
{
    public static FieldResolution ResolveText(IWebElement form, FieldSpec spec, RunOptions options)
    {
        var rejected = 0;
        var scored = new List<ScoredCandidate>();
        var index = 0;
        foreach (var control in form.FindElements(By.CssSelector("input, textarea")))
        {
            if (!DomHelpers.IsTextLike(control) || !DomHelpers.IsInteractable(control))
            {
                rejected++;
                continue;
            }

            try
            {
                scored.Add(FieldScorer.Score(control, index++, spec, form, options.Weights));
            }
            catch (StaleElementReferenceException)
            {
                rejected++;
            }
        }

        return Finish(spec.Name, scored, rejected, options);
    }

    public static FieldResolution ResolveSelect(IWebElement form, FieldSpec spec, RunOptions options)
    {
        var rejected = 0;
        var scored = new List<ScoredCandidate>();
        var index = 0;
        foreach (var control in form.FindElements(By.TagName("select")))
        {
            if (!DomHelpers.IsInteractable(control))
            {
                rejected++;
                continue;
            }

            try
            {
                scored.Add(FieldScorer.Score(control, index++, spec, form, options.Weights));
            }
            catch (StaleElementReferenceException)
            {
                rejected++;
            }
        }

        return Finish(spec.Name, scored, rejected, options);
    }

    private static FieldResolution Finish(
        string field,
        List<ScoredCandidate> scored,
        int rejected,
        RunOptions options)
    {
        var ranked = scored.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Index).ToList();
        var status = AmbiguityRule.Decide(
            ranked.Select(candidate => candidate.Score).ToList(),
            options.Threshold,
            options.Margin,
            out var reason);

        if (status == ResolutionStatus.Resolved)
        {
            ranked[0].IsWinner = true;
        }

        return new FieldResolution
        {
            Field = field,
            Status = status,
            Reason = reason,
            Winner = status == ResolutionStatus.Resolved ? ranked[0].Element : null,
            RejectedByHardFilter = rejected,
            Ranked = ranked,
        };
    }
}

public static class GenderResolver
{
    public static FieldResolution Resolve(IWebElement form, FieldSpec spec, RunOptions options)
    {
        var rejected = 0;
        var groups = new Dictionary<string, List<IWebElement>>(StringComparer.Ordinal);
        var unnamed = 0;
        foreach (var control in form.FindElements(By.TagName("input")))
        {
            if (!DomHelpers.IsRadio(control))
            {
                continue;
            }

            if (!DomHelpers.IsInteractable(control))
            {
                rejected++;
                continue;
            }

            var name = control.GetAttribute("name");
            var key = string.IsNullOrWhiteSpace(name) ? $"unnamed-{unnamed++}" : name;
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(control);
        }

        var scored = new List<ScoredCandidate>();
        var index = 0;
        foreach (var pair in groups)
        {
            try
            {
                scored.Add(FieldScorer.ScoreGroup(pair.Value, index++, pair.Key.StartsWith("unnamed-", StringComparison.Ordinal) ? "" : pair.Key, spec, options.Weights));
            }
            catch (StaleElementReferenceException)
            {
                rejected += pair.Value.Count;
            }
        }

        var ranked = scored.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Index).ToList();
        var status = AmbiguityRule.Decide(
            ranked.Select(candidate => candidate.Score).ToList(),
            options.Threshold,
            options.Margin,
            out var reason);

        IWebElement? winner = null;
        ScoredOption? option = null;
        var optionRows = new List<ScoredOption>();
        if (status == ResolutionStatus.Resolved)
        {
            ranked[0].IsWinner = true;
            var group = ranked[0];
            var match = MatchOption(group.Radios, options.Gender);
            optionRows.AddRange(match.Options);
            if (match.Status != ResolutionStatus.Resolved)
            {
                status = match.Status;
                reason = match.Reason;
            }
            else
            {
                winner = match.Winner;
                option = match.Option;
            }
        }

        return new FieldResolution
        {
            Field = spec.Name,
            Status = status,
            Reason = reason,
            Winner = winner,
            Option = option,
            RejectedByHardFilter = rejected,
            Ranked = ranked,
            Options = optionRows,
        };
    }

    public static OptionMatch MatchOption(IReadOnlyList<IWebElement> radios, string desired)
    {
        var target = TextNormalizer.Normalize(desired);
        var rows = new List<ScoredOption>();
        foreach (var radio in radios)
        {
            var value = radio.GetAttribute("value") ?? "";
            var adjacent = DomHelpers.AdjacentOptionText(radio);
            var valueMatched = target.Length > 0 && TextNormalizer.Normalize(value) == target;
            var textMatched = target.Length > 0 && TextNormalizer.Normalize(adjacent) == target;
            var score = (valueMatched ? 2 : 0) + (textMatched ? 2 : 0);
            rows.Add(new ScoredOption
            {
                Element = radio,
                Score = score,
                Value = value,
                AdjacentText = adjacent,
                ValueMatched = valueMatched,
                TextMatched = textMatched,
                Description = $"value='{value}' adjacent='{adjacent}'",
            });
        }

        var ranked = rows.OrderByDescending(row => row.Score).ToList();
        if (ranked.Count == 0 || ranked[0].Score < 2)
        {
            return new OptionMatch(
                ResolutionStatus.OptionNotFound,
                $"no radio has value or adjacent text '{desired}'",
                null,
                null,
                ranked);
        }

        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < 1)
        {
            return new OptionMatch(
                ResolutionStatus.Ambiguous,
                $"more than one radio matches '{desired}' ({ranked[0].Description} and {ranked[1].Description})",
                null,
                null,
                ranked);
        }

        return new OptionMatch(
            ResolutionStatus.Resolved,
            $"matched '{desired}' by " + (ranked[0].ValueMatched ? "value" : "adjacent text"),
            ranked[0].Element,
            ranked[0],
            ranked);
    }

    public sealed record OptionMatch(
        ResolutionStatus Status,
        string Reason,
        IWebElement? Winner,
        ScoredOption? Option,
        IReadOnlyList<ScoredOption> Options);
}
