using SelfHealing.Core;

// Fills First Name, Gender and State on the CloudQA practice form (or a
// modified copy of it). The URL and the three values are configurable.
// Submit is never clicked.

try
{
    var options = SettingsLoader.Load(args);
    if (options.Help)
    {
        Console.WriteLine(HelpText.Help);
        return 0;
    }

    if (string.IsNullOrWhiteSpace(options.FirstName) || string.IsNullOrWhiteSpace(options.Gender) || string.IsNullOrWhiteSpace(options.State))
    {
        Console.Error.WriteLine("First name, gender and state all need a value. See --help.");
        return 1;
    }

    using var session = BrowserFactory.Open(options.Headless);
    var report = new FormAutomation().Execute(session.Driver, options);
    ReportPrinter.Write(Console.Out, report);
    if (!string.IsNullOrWhiteSpace(options.ReportPath))
    {
        Console.WriteLine();
        Console.WriteLine("Report: " + Path.GetFullPath(options.ReportPath));
    }

    if (!options.Headless)
    {
        Console.WriteLine();
        Console.WriteLine("[Visible Mode] Browser will stay open for 15 seconds so you can inspect the filled form...");
        Thread.Sleep(15000);
    }

    return report.ExitCode;
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

internal static class HelpText
{
    public const string Help = """
        CloudQA self-healing form filler

        Usage:
          dotnet run --project src/SelfHealing.Runner -- [url] [options]

        Options:
          --url <url>            Page to open. Overrides CLOUDQA_URL and appsettings.json.
          --firstname <value>    Text for the First Name field. Default: Ishan.
          --gender <value>       Male, Female or Transgender. Default: Female.
          --state <value>        Option text or value, for example India. Default: India.
          --headless <bool>      true (default) or false.
          --timeout <seconds>    How long to wait for a form. Default: 30.
          --report <path>        JSON report. Default: reports/execution-report.json.
          --help                 Show this text.

        A bare URL is accepted as well as --url. The environment variables
        CLOUDQA_URL, CLOUDQA_FIRSTNAME, CLOUDQA_GENDER and CLOUDQA_STATE sit
        between the command line and appsettings.json.

        Exit codes:
          0  the three fields were filled and verified
          2  a field or the form was not found
          3  the match was ambiguous
          4  the option was missing, or the value did not stick
          1  anything else

        The program fills only First Name, Gender and State. It does not click Submit.
        """;
}
