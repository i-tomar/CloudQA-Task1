using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SelfHealing.Core;

namespace SelfHealing.Tests;

/// <summary>
/// One browser, every local fixture, then the live practice form.
/// The run fills First Name, Gender and State only. Submit is never clicked.
/// </summary>
[TestFixture]
[NonParallelizable]
[CancelAfter(180000)]
public class PracticeFormTests
{
    private const string LiveUrl = "https://app.cloudqa.io/home/AutomationPracticeForm";

    private ChromeSession _session = null!;
    private RunOptions _defaults = null!;
    private readonly FormAutomation _automation = new();

    [OneTimeSetUp]
    public void StartBrowser()
    {
        _defaults = SettingsLoader.Load([], RepoLocator.Find()) with { ReportPath = null, Headless = true };
        _session = BrowserFactory.Open(headless: true);
    }

    [OneTimeTearDown]
    public void StopBrowser() => _session?.Dispose();

    [TestCase("original.html")]
    [TestCase("reordered.html")]
    [TestCase("renamed-ids-names.html")]
    [TestCase("renamed-classes.html")]
    [TestCase("restructured.html")]
    [TestCase("decoys.html")]
    public void Variant_FillsTheThreeFields(string file)
    {
        var report = Run(file);
        AssertPassed(report);
        var probe = ProbePage();
        AssertFilled(probe, "Ishan", "Female", "India");

        var first = Field(report, "First Name");
        if (file == "restructured.html")
        {
            Assert.That(first.DecidingSignals.Any(signal => signal.Name == "nearby-text" && signal.Evidence == "First Name"), Is.True, ReportPrinter.Format(report));
        }
        else
        {
            Assert.That(first.DecidingSignals.Any(signal => signal.Name == "label" && signal.Evidence == "First Name"), Is.True, ReportPrinter.Format(report));
        }

        if (file == "renamed-ids-names.html")
        {
            Assert.That(first.DecidingSignals.All(signal => signal.Name != "id-or-name"), Is.True, ReportPrinter.Format(report));
        }

        if (file == "decoys.html")
        {
            Assert.That(first.Score - first.RunnerUpScore, Is.GreaterThanOrEqualTo(_defaults.Margin), ReportPrinter.Format(report));
            Assert.That(
                first.Candidates.Any(candidate => candidate.Signals.Any(signal => (signal.Evidence ?? "").Contains("Emergency", StringComparison.Ordinal))),
                Is.True,
                ReportPrinter.Format(report));
        }
    }

    [Test]
    public void Original_UsesTheFirstForm_AndIgnoresDuplicatesIframeAndHiddenFields()
    {
        var report = Run("original.html");
        AssertPassed(report);
        Assert.That(report.ChosenForm!.Id, Is.EqualTo("automationtestform"));
        Assert.That(report.ChosenForm.Index, Is.EqualTo(0));
        Assert.That(report.Forms.Count(form => form.Score == report.ChosenForm.Score), Is.GreaterThanOrEqualTo(2));
        Assert.That(report.HiddenFormsSkipped, Is.GreaterThanOrEqualTo(1));
        Assert.That(report.IframesNotEntered, Is.GreaterThanOrEqualTo(1));

        Assert.That(Value("#automationtestform input[name='First Name']"), Is.EqualTo("Ishan"));
        Assert.That(Value("#automationtestform input[name='Last Name']"), Is.EqualTo(""));
        Assert.That(Value("#duplicate-form input[name='First Name']"), Is.EqualTo(""));
        Assert.That(Value("#hidden-form input"), Is.EqualTo(""));
        Assert.That(Value("#secret-first"), Is.EqualTo(""));
        Assert.That(CheckedInDocument(), Is.EqualTo(new[] { "Female" }));

        var frame = _session.Driver.FindElement(By.CssSelector("#trap-frame"));
        _session.Driver.SwitchTo().Frame(frame);
        new WebDriverWait(_session.Driver, TimeSpan.FromSeconds(5)).Until(driver => driver.FindElements(By.CssSelector("input")).Count > 0);
        Assert.That(_session.Driver.FindElement(By.CssSelector("input")).GetDomProperty("value") ?? "", Is.EqualTo(""));
        _session.Driver.SwitchTo().DefaultContent();
    }

    [TestCase("Asha", "Transgender", "Afganistan")]
    [TestCase("Mira", "Male", "United States")]
    [TestCase("Ishan", "female", "india")]
    public void Original_SelectsByValueOrText_NotByIndex(string firstName, string gender, string state)
    {
        var report = Run("original.html", _defaults with { FirstName = firstName, Gender = gender, State = state });
        AssertPassed(report);
        var probe = ProbePage();
        Assert.That(NonEmpty(probe.TextValues), Is.EqualTo(new[] { firstName }));
        Assert.That(probe.Checked, Is.EqualTo(new[] { CanonicalGender(gender) }));
        Assert.That(probe.Selects.Count(value => SelectionMatches(value, state)), Is.EqualTo(1), ReportPrinter.Format(report));
        var genderField = Field(report, "Gender");
        Assert.That(genderField.Detail, Does.Contain("Span for= attributes are not read"));
    }

    [Test]
    public void AmbiguousFirstName_FailsAndDoesNotFill()
    {
        var report = Run("ambiguous-firstname.html");
        Assert.That(report.Status, Is.EqualTo("ambiguous"), ReportPrinter.Format(report));
        Assert.That(report.Success, Is.False);
        var first = Field(report, "First Name");
        Assert.That(first.Status, Is.EqualTo("ambiguous"));
        Assert.That(first.Score, Is.EqualTo(first.RunnerUpScore));
        Assert.That(first.Candidates.Count(candidate => candidate.Signals.Any(signal => signal.Evidence == "First Name")), Is.GreaterThanOrEqualTo(2));

        var probe = ProbePage();
        Assert.That(NonEmpty(probe.TextValues), Is.Empty);
        Assert.That(probe.Checked, Is.Empty);
        Assert.That(probe.SubmitClicks, Is.EqualTo(0));
        Assert.That(probe.Submitted, Is.False);
    }

    [Test]
    public void MissingFirstName_FailsAsNotFound_AndDoesNotFillTheOthers()
    {
        var report = Run("no-firstname.html");
        Assert.That(report.Status, Is.EqualTo("not-found"), ReportPrinter.Format(report));
        var first = Field(report, "First Name");
        Assert.That(first.Status, Is.EqualTo("not-found"));
        Assert.That(Field(report, "Gender").Status, Is.EqualTo("resolved"));
        Assert.That(Field(report, "State").Status, Is.EqualTo("resolved"));

        var probe = ProbePage();
        Assert.That(NonEmpty(probe.TextValues), Is.Empty);
        Assert.That(probe.Checked, Is.Empty);
        Assert.That(probe.Selects.Count(value => SelectionMatches(value, "India")), Is.EqualTo(0));
    }

    [Test]
    public void MissingStateOption_FailsBeforeTyping()
    {
        var report = Run("original.html", _defaults with { State = "Narnia" });
        Assert.That(report.Status, Is.EqualTo("option-not-found"), ReportPrinter.Format(report));
        Assert.That(Field(report, "State").Status, Is.EqualTo("option-not-found"));
        var probe = ProbePage();
        Assert.That(NonEmpty(probe.TextValues), Is.Empty, "the first name must not be typed once State has no matching option");
        Assert.That(probe.Checked, Is.Empty);
    }

    [Test]
    public void MissingGenderOption_FailsBeforeTyping()
    {
        var report = Run("original.html", _defaults with { Gender = "Other" });
        Assert.That(report.Status, Is.EqualTo("option-not-found"), ReportPrinter.Format(report));
        Assert.That(NonEmpty(ProbePage().TextValues), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MutatedPage_StillPasses(int seed)
    {
        var original = File.ReadAllText(Path.Combine(RepoLocator.Find(), "test-pages", "original.html"));
        var mutated = PageMutator.Mutate(original, seed);
        var path = Path.Combine(Path.GetTempPath(), $"cloudqa-mut-{seed}.html");
        File.WriteAllText(path, mutated);

        var report = RunPath(path);
        AssertPassed(report);
        var probe = ProbePage();
        AssertFilled(probe, "Ishan", "Female", "India");
        Assert.That(report.ChosenForm!.Index, Is.EqualTo(0), ReportPrinter.Format(report));
    }

    [Test]
    public void ExecutionReport_IsWrittenAsJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "cloudqa-report-" + Guid.NewGuid().ToString("N") + ".json");
        var report = Run("original.html", _defaults with { ReportPath = path });
        AssertPassed(report);
        Assert.That(File.Exists(path), Is.True);
        var saved = JsonSerializer.Deserialize<ExecutionReport>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Status, Is.EqualTo("pass"));
        Assert.That(saved.Fields.Single(field => field.Field == "First Name").DecidingSignals.Any(signal => signal.Name == "label"), Is.True);
        File.Delete(path);
    }

    [Test]
    public void LivePage_FillsThreeFields_AndDoesNotSubmit()
    {
        var report = RunUrl(LiveUrl, _defaults with { TimeoutSeconds = 60 });
        AssertPassed(report);
        Assert.That(report.ChosenForm!.Id, Is.EqualTo("automationtestform"), ReportPrinter.Format(report));
        Assert.That(report.IframesNotEntered, Is.GreaterThanOrEqualTo(1));
        Assert.That(report.ShadowHostsNotEntered, Is.GreaterThan(0), ReportPrinter.Format(report));
        Assert.That(_session.Driver.Url, Does.Contain("AutomationPracticeForm"));

        Assert.That(Value("#automationtestform input[name='First Name']"), Is.EqualTo("Ishan"));
        Assert.That(Value("#automationtestform input[name='Last Name']"), Is.EqualTo(""));
        Assert.That(Script("return document.querySelector('#automationtestform input#female').checked;"), Is.EqualTo("True"));
        var state = Script("""
            var select = document.querySelector('#automationtestform select');
            var option = select.options[select.selectedIndex];
            return (option.value || '') + '|' + (option.text || '');
            """);
        Assert.That(SelectionMatches(state, "India"), Is.True, state);

        Assert.That(Script("return Array.from(document.querySelectorAll('input')).filter(function (el) { return el.value === 'Ishan'; }).length;"), Is.EqualTo("1"));
        Assert.That(Script("return Array.from(document.querySelectorAll(\"input[type='radio']:checked\")).map(function (el) { return el.value; }).join(',');"), Is.EqualTo("Female"));
        Assert.That(Script("""
            return Array.from(document.querySelectorAll('select')).filter(function (select) {
              var option = select.options[select.selectedIndex];
              if (!option) return false;
              var value = (option.value || '').replace(/\s+/g, ' ').trim().toLowerCase();
              var text = (option.text || '').replace(/\s+/g, ' ').trim().toLowerCase();
              return value === 'india' || text === 'india';
            }).length;
            """), Is.EqualTo("1"));

        var probe = ProbePage();
        Assert.That(probe.SubmitClicks, Is.EqualTo(0));
        Assert.That(probe.Submitted, Is.False);
    }

    private ExecutionReport Run(string file, RunOptions? basis = null)
    {
        var path = Path.Combine(RepoLocator.Find(), "test-pages", file);
        return RunPath(path, basis);
    }

    private ExecutionReport RunPath(string path, RunOptions? basis = null) =>
        RunUrl(new Uri(Path.GetFullPath(path)).AbsoluteUri, basis);

    private ExecutionReport RunUrl(string url, RunOptions? basis = null)
    {
        var options = (basis ?? _defaults) with
        {
            Url = url,
            Headless = true,
            AfterPageLoad = InstallSubmitGuard,
        };
        return _automation.Execute(_session.Driver, options);
    }

    private static void InstallSubmitGuard(IWebDriver driver)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript("""
            window.__submitClicks = 0;
            document.querySelectorAll('form').forEach(function (form) {
              form.addEventListener('submit', function () { window.__submitClicks++; }, true);
            });
            """);
    }

    private PageProbe ProbePage()
    {
        var json = Script("""
            var form = document.forms[0];
            var textValues = Array.from(form.querySelectorAll('input, textarea')).filter(function (el) {
              var type = (el.getAttribute('type') || 'text').toLowerCase();
              return ['radio', 'checkbox', 'hidden', 'number', 'file', 'submit', 'button', 'reset'].indexOf(type) < 0;
            }).map(function (el) { return el.value; });
            var checked = Array.from(form.querySelectorAll('input[type=radio]:checked')).map(function (el) { return el.value; });
            var selects = Array.from(form.querySelectorAll('select')).map(function (select) {
              var option = select.options[select.selectedIndex];
              return option ? ((option.value || '') + '|' + (option.text || '')) : '';
            });
            return JSON.stringify({
              textValues: textValues,
              checked: checked,
              selects: selects,
              submitted: window.__submitted === true,
              submitClicks: window.__submitClicks || 0
            });
            """);
        return JsonSerializer.Deserialize<PageProbe>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Could not read the page back.");
    }

    private string Value(string cssSelector) =>
        Script($"return document.querySelector({JsonSerializer.Serialize(cssSelector)}).value || '';");

    private string Script(string script) =>
        ((IJavaScriptExecutor)_session.Driver).ExecuteScript(script)?.ToString() ?? "";

    private string[] CheckedInDocument() =>
        Script("return Array.from(document.querySelectorAll(\"input[type='radio']:checked\")).map(function (el) { return el.value; }).join('|');")
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

    private static void AssertPassed(ExecutionReport report) =>
        Assert.That(report.Success, Is.True, ReportPrinter.Format(report) + Environment.NewLine + report.Error);

    private static void AssertFilled(PageProbe probe, string firstName, string gender, string state)
    {
        Assert.That(probe.Submitted, Is.False);
        Assert.That(probe.SubmitClicks, Is.EqualTo(0));
        Assert.That(NonEmpty(probe.TextValues), Is.EqualTo(new[] { firstName }));
        Assert.That(probe.Checked, Is.EqualTo(new[] { gender }));
        Assert.That(probe.Selects.Count(value => SelectionMatches(value, state)), Is.EqualTo(1), string.Join(" ; ", probe.Selects));
    }

    private static FieldReport Field(ExecutionReport report, string name) =>
        report.Fields.Single(field => field.Field == name);

    private static List<string> NonEmpty(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrEmpty(value)).ToList();

    private static bool SelectionMatches(string raw, string expected)
    {
        var target = TextNormalizer.Normalize(expected);
        return raw.Split('|').Any(part => TextNormalizer.Normalize(part) == target);
    }

    private static string CanonicalGender(string gender) => gender.ToLowerInvariant() switch
    {
        "male" => "Male",
        "female" => "Female",
        "transgender" => "Transgender",
        _ => gender,
    };

    private sealed class PageProbe
    {
        public List<string> TextValues { get; set; } = [];

        public List<string> Checked { get; set; } = [];

        public List<string> Selects { get; set; } = [];

        public bool Submitted { get; set; }

        public int SubmitClicks { get; set; }
    }
}
