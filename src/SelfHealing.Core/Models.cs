namespace SelfHealing.Core;

public enum FieldKind
{
    Text,
    RadioGroup,
    Select,
}

/// <summary>
/// What one requested field looks like. The synonyms and the penalty list
/// come from configuration so a reviewer can retune them without a code change.
/// </summary>
public sealed class FieldSpec
{
    public required string Name { get; init; }

    public required FieldKind Kind { get; init; }

    public required IReadOnlyList<string> Synonyms { get; init; }

    public IReadOnlyList<string> PenaltyTerms { get; init; } = [];

    /// <summary>
    /// Placeholder values that count as the weak placeholder hint even though
    /// they are too broad to be synonyms. The live First Name box says "Name".
    /// </summary>
    public IReadOnlyList<string> PlaceholderHints { get; init; } = [];
}

public sealed class SignalWeights
{
    public int Label { get; init; } = 50;

    public int AccessibleName { get; init; } = 34;

    public int NearbyText { get; init; } = 22;

    public int Placeholder { get; init; } = 10;

    public int IdOrName { get; init; } = 6;

    public int Penalty { get; init; } = 28;

    public IReadOnlyDictionary<string, int> AsMap() => new Dictionary<string, int>
    {
        ["label"] = Label,
        ["accessible-name"] = AccessibleName,
        ["nearby-text"] = NearbyText,
        ["placeholder"] = Placeholder,
        ["id-or-name"] = IdOrName,
        ["penalty"] = Penalty,
    };
}

public static class SignalNames
{
    public const string Label = "label";
    public const string AccessibleName = "accessible-name";
    public const string NearbyText = "nearby-text";
    public const string Placeholder = "placeholder";
    public const string IdOrName = "id-or-name";
    public const string Penalty = "penalty";
}

public sealed class SignalContribution
{
    public required string Name { get; init; }

    public int Points { get; init; }

    public string? Evidence { get; init; }
}

/// <summary>
/// One element (or one radio group) after scoring. The element reference is
/// kept so the automation can fill the winner; the report is built from the
/// plain data beside it.
/// </summary>
public sealed class ScoredCandidate
{
    public required IWebElement Element { get; init; }

    public int Index { get; init; }

    public int Score { get; set; }

    public string Description { get; init; } = "";

    public List<SignalContribution> Signals { get; } = [];

    /// <summary>Radio group only: the radios that belong to this candidate.</summary>
    public List<IWebElement> Radios { get; } = [];

    public string GroupName { get; init; } = "";

    public bool IsWinner { get; set; }
}

public sealed class ScoredOption
{
    public required IWebElement Element { get; init; }

    public int Score { get; init; }

    public string? Value { get; init; }

    public string? AdjacentText { get; init; }

    public bool ValueMatched { get; init; }

    public bool TextMatched { get; init; }

    public string Description { get; init; } = "";
}

public sealed record RunOptions
{
    public string Url { get; init; } = "";

    public bool Headless { get; init; } = true;

    public int TimeoutSeconds { get; init; } = 30;

    public string FirstName { get; init; } = "Ishan";

    public string Gender { get; init; } = "Female";

    public string State { get; init; } = "India";

    public int Threshold { get; init; } = 14;

    public int Margin { get; init; } = 12;

    public SignalWeights Weights { get; init; } = new();

    public IReadOnlyList<string> FirstNameSynonyms { get; init; } = DefaultFirstNameSynonyms;

    public IReadOnlyList<string> GenderSynonyms { get; init; } = DefaultGenderSynonyms;

    public IReadOnlyList<string> StateSynonyms { get; init; } = DefaultStateSynonyms;

    public IReadOnlyList<string> LastNamePenalties { get; init; } = DefaultPenalties;

    public string? ReportPath { get; init; }

    public bool Help { get; init; }

    /// <summary>
    /// Invoked after navigation, before any field is resolved. Tests use it to
    /// install a submit listener. The runner leaves it null.
    /// </summary>
    public Action<IWebDriver>? AfterPageLoad { get; init; }

    public FieldSpec FirstNameSpec => new()
    {
        Name = "First Name",
        Kind = FieldKind.Text,
        Synonyms = FirstNameSynonyms,
        PenaltyTerms = LastNamePenalties,
        PlaceholderHints = ["name"],
    };

    public FieldSpec GenderSpec => new()
    {
        Name = "Gender",
        Kind = FieldKind.RadioGroup,
        Synonyms = GenderSynonyms,
    };

    public FieldSpec StateSpec => new()
    {
        Name = "State",
        Kind = FieldKind.Select,
        Synonyms = StateSynonyms,
    };

    public static readonly string[] DefaultFirstNameSynonyms =
    [
        "first name", "firstname", "given name", "givenname", "forename", "fname",
    ];

    public static readonly string[] DefaultGenderSynonyms = ["gender", "sex"];

    public static readonly string[] DefaultStateSynonyms = ["state", "province"];

    public static readonly string[] DefaultPenalties =
    [
        "last name", "lastname", "surname", "lname", "family name",
    ];
}
