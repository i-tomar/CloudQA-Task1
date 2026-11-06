namespace SelfHealing.Core;

public sealed class ExecutionReport
{
    public string Url { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Status { get; set; } = "error";

    public bool Success { get; set; }

    public string? Error { get; set; }

    public string ScopeNote { get; set; } =
        "Only visible forms in the top-level document are searched. Iframes and shadow DOM are not entered. " +
        "If several forms tie, the earliest in document order is treated as the main form.";

    public int Threshold { get; set; }

    public int Margin { get; set; }

    public Dictionary<string, int> Weights { get; set; } = [];

    public int HiddenFormsSkipped { get; set; }

    public int IframesNotEntered { get; set; }

    public int ShadowHostsNotEntered { get; set; }

    public FormReport? ChosenForm { get; set; }

    public List<FormReport> Forms { get; set; } = [];

    public List<FieldReport> Fields { get; set; } = [];

    public int ExitCode => Status switch
    {
        "pass" => 0,
        "not-found" => 2,
        "ambiguous" => 3,
        "option-not-found" or "verification-failed" => 4,
        _ => 1,
    };
}

public sealed class FormReport
{
    public int Index { get; set; }

    public int Score { get; set; }

    public bool Chosen { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? ClassName { get; set; }

    public bool HasTextInput { get; set; }

    public bool HasRadio { get; set; }

    public bool HasSelect { get; set; }
}

public sealed class FieldReport
{
    public string Field { get; set; } = "";

    public string Status { get; set; } = "";

    public int? Score { get; set; }

    public int? RunnerUpScore { get; set; }

    public string? Reason { get; set; }

    public string? Matched { get; set; }

    public string? FilledValue { get; set; }

    public string? Detail { get; set; }

    public int RejectedByHardFilter { get; set; }

    public List<SignalReport> DecidingSignals { get; set; } = [];

    public List<CandidateReport> Candidates { get; set; } = [];
}

public sealed class SignalReport
{
    public string Name { get; set; } = "";

    public int Points { get; set; }

    public string? Evidence { get; set; }
}

public sealed class CandidateReport
{
    public int Index { get; set; }

    public int Score { get; set; }

    public string Description { get; set; } = "";

    public bool Winner { get; set; }

    public List<SignalReport> Signals { get; set; } = [];
}
