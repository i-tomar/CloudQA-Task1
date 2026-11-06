namespace SelfHealing.Core;

public enum ResolutionStatus
{
    Resolved,
    Ambiguous,
    NotFound,
    OptionNotFound,
    Verified,
    VerificationFailed,
    Error,
}

/// <summary>
/// Picks a winner only when the top score clears <paramref name="threshold"/>
/// and leads the runner-up by at least <paramref name="margin"/>.
/// A tie, or a lead smaller than the margin, is ambiguous rather than a guess.
/// A top score under the threshold is not-found, even if it is the only candidate.
/// </summary>
public static class AmbiguityRule
{
    public static ResolutionStatus Decide(
        IReadOnlyList<int> scoresHighToLow,
        int threshold,
        int margin,
        out string reason)
    {
        if (scoresHighToLow.Count == 0 || scoresHighToLow[0] < threshold)
        {
            reason = scoresHighToLow.Count == 0
                ? "no candidate passed the hard filters"
                : $"top score {scoresHighToLow[0]} is below the threshold {threshold}";
            return ResolutionStatus.NotFound;
        }

        if (scoresHighToLow.Count > 1 && scoresHighToLow[0] - scoresHighToLow[1] < margin)
        {
            reason =
                $"top score {scoresHighToLow[0]} does not lead the runner-up {scoresHighToLow[1]} by the margin {margin}";
            return ResolutionStatus.Ambiguous;
        }

        var lead = scoresHighToLow.Count == 1
            ? "there is no runner-up"
            : $"it leads the runner-up {scoresHighToLow[1]} by {scoresHighToLow[0] - scoresHighToLow[1]}";
        reason = $"top score {scoresHighToLow[0]} clears the threshold {threshold} and {lead}";
        return ResolutionStatus.Resolved;
    }

    public static string ToToken(ResolutionStatus status) => status switch
    {
        ResolutionStatus.Resolved => "resolved",
        ResolutionStatus.Ambiguous => "ambiguous",
        ResolutionStatus.NotFound => "not-found",
        ResolutionStatus.OptionNotFound => "option-not-found",
        ResolutionStatus.Verified => "verified",
        ResolutionStatus.VerificationFailed => "verification-failed",
        _ => "error",
    };
}
