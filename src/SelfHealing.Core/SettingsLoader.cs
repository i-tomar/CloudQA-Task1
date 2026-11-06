namespace SelfHealing.Core;

/// <summary>
/// URL precedence is command line, then the CLOUDQA_URL environment variable,
/// then appsettings.json. Test data follows the same idea: command line, then
/// CLOUDQA_FIRSTNAME / CLOUDQA_GENDER / CLOUDQA_STATE, then the json file.
/// </summary>
public static class SettingsLoader
{
    public static RunOptions Load(string[] args, string? contentRoot = null)
    {
        var root = contentRoot ?? RepoLocator.Find();
        var jsonPath = Path.Combine(root, "appsettings.json");
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException("Could not find appsettings.json.", jsonPath);
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile(jsonPath, optional: false, reloadOnChange: false)
            .Build();

        var bound = new CloudQaConfig();
        config.GetSection("CloudQa").Bind(bound);
        var cli = Cli.Parse(args);

        var envUrl = Environment.GetEnvironmentVariable("CLOUDQA_URL");
        var url = First(
            cli.Url,
            string.IsNullOrWhiteSpace(envUrl) ? null : envUrl.Trim(),
            bound.Url);

        return new RunOptions
        {
            Url = url ?? "",
            Headless = cli.Headless ?? bound.Headless,
            TimeoutSeconds = cli.TimeoutSeconds ?? (bound.TimeoutSeconds <= 0 ? 30 : bound.TimeoutSeconds),
            FirstName = First(cli.FirstName, Environment.GetEnvironmentVariable("CLOUDQA_FIRSTNAME"), bound.FirstName) ?? "Ishan",
            Gender = First(cli.Gender, Environment.GetEnvironmentVariable("CLOUDQA_GENDER"), bound.Gender) ?? "Female",
            State = First(cli.State, Environment.GetEnvironmentVariable("CLOUDQA_STATE"), bound.State) ?? "India",
            Threshold = bound.Threshold <= 0 ? 14 : bound.Threshold,
            Margin = bound.Margin <= 0 ? 12 : bound.Margin,
            Weights = new SignalWeights
            {
                Label = Or(bound.Weights?.Label, 50),
                AccessibleName = Or(bound.Weights?.AccessibleName, 34),
                NearbyText = Or(bound.Weights?.NearbyText, 22),
                Placeholder = Or(bound.Weights?.Placeholder, 10),
                IdOrName = Or(bound.Weights?.IdOrName, 6),
                Penalty = Or(bound.Weights?.Penalty, 28),
            },
            FirstNameSynonyms = OrList(bound.Synonyms?.FirstName, RunOptions.DefaultFirstNameSynonyms),
            GenderSynonyms = OrList(bound.Synonyms?.Gender, RunOptions.DefaultGenderSynonyms),
            StateSynonyms = OrList(bound.Synonyms?.State, RunOptions.DefaultStateSynonyms),
            LastNamePenalties = OrList(bound.Synonyms?.LastNamePenalty, RunOptions.DefaultPenalties),
            ReportPath = cli.ReportPath ?? bound.ReportPath,
            Help = cli.Help,
        };
    }

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int Or(int? value, int fallback) => value is null or 0 ? fallback : value.Value;

    private static IReadOnlyList<string> OrList(List<string>? value, IReadOnlyList<string> fallback) =>
        value is { Count: > 0 } ? value : fallback;

    private sealed class CloudQaConfig
    {
        public string? Url { get; set; }

        public bool Headless { get; set; } = true;

        public int TimeoutSeconds { get; set; } = 30;

        public string? FirstName { get; set; }

        public string? Gender { get; set; }

        public string? State { get; set; }

        public int Threshold { get; set; } = 14;

        public int Margin { get; set; } = 12;

        public string? ReportPath { get; set; }

        public WeightConfig? Weights { get; set; }

        public SynonymConfig? Synonyms { get; set; }
    }

    private sealed class WeightConfig
    {
        public int? Label { get; set; }

        public int? AccessibleName { get; set; }

        public int? NearbyText { get; set; }

        public int? Placeholder { get; set; }

        public int? IdOrName { get; set; }

        public int? Penalty { get; set; }
    }

    private sealed class SynonymConfig
    {
        public List<string>? FirstName { get; set; }

        public List<string>? Gender { get; set; }

        public List<string>? State { get; set; }

        public List<string>? LastNamePenalty { get; set; }
    }
}

public static class RepoLocator
{
    public static string Find()
    {
        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var json = Path.Combine(dir.FullName, "appsettings.json");
                var slnx = Path.Combine(dir.FullName, "cloudqa-selfhealing.slnx");
                if (File.Exists(json) && File.Exists(slnx))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the repo root (a directory containing appsettings.json and cloudqa-selfhealing.slnx). Run from the repository, or pass the URL on the command line after building.");
    }
}

internal sealed class Cli
{
    public string? Url { get; private set; }

    public string? FirstName { get; private set; }

    public string? Gender { get; private set; }

    public string? State { get; private set; }

    public bool? Headless { get; private set; }

    public int? TimeoutSeconds { get; private set; }

    public string? ReportPath { get; private set; }

    public bool Help { get; private set; }

    public static Cli Parse(string[] args)
    {
        var cli = new Cli();
        string? positional = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    cli.Help = true;
                    break;
                case "--url":
                    cli.Url = Next(args, ref i, arg);
                    break;
                case "--firstname" or "--first-name":
                    cli.FirstName = Next(args, ref i, arg);
                    break;
                case "--gender":
                    cli.Gender = Next(args, ref i, arg);
                    break;
                case "--state":
                    cli.State = Next(args, ref i, arg);
                    break;
                case "--headless":
                    cli.Headless = i + 1 < args.Length && !args[i + 1].StartsWith('-')
                        ? ParseBool(Next(args, ref i, arg))
                        : true;
                    break;
                case "--timeout":
                    var raw = Next(args, ref i, arg);
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                    {
                        throw new ArgumentException("--timeout expects a positive number of seconds.");
                    }

                    cli.TimeoutSeconds = seconds;
                    break;
                case "--report":
                    cli.ReportPath = Next(args, ref i, arg);
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown argument '{arg}'. Use --help to see the supported flags.");
                    }

                    if (positional != null)
                    {
                        throw new ArgumentException($"Unexpected argument '{arg}'.");
                    }

                    positional = arg;
                    break;
            }
        }

        cli.Url ??= positional;
        return cli;
    }

    private static string Next(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {flag}.");
        }

        index++;
        return args[index];
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value switch
        {
            "1" => true,
            "0" => false,
            _ => throw new ArgumentException("--headless expects true or false."),
        };
    }
}
