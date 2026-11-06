namespace SelfHealing.Core;

/// <summary>
/// Navigates to the configured URL, scopes to the main form, resolves the three
/// fields, and only then fills them. If any field is ambiguous or missing, the
/// page is left untouched. Submit is never clicked.
/// </summary>
public sealed class FormAutomation
{
    public ExecutionReport Execute(IWebDriver driver, RunOptions options)
    {
        var report = new ExecutionReport
        {
            Url = options.Url,
            StartedAt = DateTimeOffset.UtcNow,
            Threshold = options.Threshold,
            Margin = options.Margin,
            Weights = options.Weights.AsMap().ToDictionary(pair => pair.Key, pair => pair.Value),
        };

        try
        {
            if (string.IsNullOrWhiteSpace(options.Url))
            {
                return Fail(report, "error", "No URL was configured. Pass --url, set CLOUDQA_URL, or set CloudQa:Url in appsettings.json.");
            }

            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));
            driver.Navigate().GoToUrl(options.Url);
            WaitForForm(driver, options.TimeoutSeconds);
            options.AfterPageLoad?.Invoke(driver);

            var scope = FormScoper.Choose(driver);
            report.Forms = scope.Forms;
            report.ChosenForm = scope.Chosen;
            report.HiddenFormsSkipped = scope.HiddenFormsSkipped;
            report.IframesNotEntered = scope.IframesNotEntered;
            report.ShadowHostsNotEntered = scope.ShadowHostsNotEntered;

            if (scope.ChosenElement == null)
            {
                return Fail(report, "not-found", "not-found: no visible form in the top-level document.");
            }

            var first = ElementResolver.ResolveText(scope.ChosenElement, options.FirstNameSpec, options);
            var gender = GenderResolver.Resolve(scope.ChosenElement, options.GenderSpec, options);
            var state = ElementResolver.ResolveSelect(scope.ChosenElement, options.StateSpec, options);
            var firstReport = first.ToReport();
            var genderReport = gender.ToReport();
            var stateReport = state.ToReport();
            report.Fields.Add(firstReport);
            report.Fields.Add(genderReport);
            report.Fields.Add(stateReport);

            var blocking = new[] { first, gender, state }
                .Where(resolution => resolution.Status != ResolutionStatus.Resolved)
                .ToList();
            if (blocking.Count > 0)
            {
                var status = BlockingStatus(blocking.Select(resolution => resolution.Status));
                return Fail(report, status, string.Join(Environment.NewLine, blocking.Select(resolution => resolution.Summary)));
            }

            // Resolve the state option before editing anything, so a missing
            // country does not leave a half-filled form.
            IWebElement stateOption;
            try
            {
                stateOption = FieldActions.FindOption(state.Winner!, options.State);
            }
            catch (AutomationException ex)
            {
                return FailField(report, stateReport, ex);
            }

            try
            {
                firstReport.FilledValue = FieldActions.FillText(driver, first.Winner!, options.FirstName);
                firstReport.Status = "verified";
                if (!options.Headless) Thread.Sleep(1200);
            }
            catch (AutomationException ex)
            {
                return FailField(report, firstReport, ex);
            }

            try
            {
                FieldActions.ClickRadio(driver, gender.Winner!, options.Gender);
                genderReport.FilledValue = options.Gender;
                genderReport.Status = "verified";
                if (!options.Headless) Thread.Sleep(1200);
            }
            catch (AutomationException ex)
            {
                return FailField(report, genderReport, ex);
            }

            try
            {
                stateReport.FilledValue = FieldActions.ApplyOption(driver, state.Winner!, stateOption, options.State);
                stateReport.Status = "verified";
                if (!options.Headless) Thread.Sleep(1200);
            }
            catch (AutomationException ex)
            {
                return FailField(report, stateReport, ex);
            }

            report.Success = true;
            report.Status = "pass";
            report.Error = null;
            return report;
        }
        catch (AutomationException ex)
        {
            var token = AmbiguityRule.ToToken(ex.Status);
            var pending = report.Fields.LastOrDefault(field => field.Status == "resolved");
            if (pending != null)
            {
                pending.Status = token;
                pending.Detail = ex.Message;
            }

            report.Success = false;
            report.Status = token;
            report.Error = ex.Message;
            return report;
        }
        catch (WebDriverTimeoutException ex)
        {
            return Fail(report, "not-found", "Timed out waiting for a visible form. " + ex.Message);
        }
        catch (Exception ex) when (ex is WebDriverException or InvalidOperationException or UriFormatException)
        {
            return Fail(report, "error", ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                SaveReport(report, options.ReportPath);
            }
        }
    }

    public static void SaveReport(ExecutionReport report, string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(full, json);
    }

    private static void WaitForForm(IWebDriver driver, int timeoutSeconds)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)))
        {
            PollingInterval = TimeSpan.FromMilliseconds(200),
        };
        wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        wait.Until(d =>
        {
            try
            {
                return d.FindElements(By.TagName("form")).Any(DomHelpers.IsDisplayed);
            }
            catch (WebDriverException)
            {
                return false;
            }
        });
    }

    private static ExecutionReport FailField(ExecutionReport report, FieldReport field, AutomationException ex)
    {
        field.Status = AmbiguityRule.ToToken(ex.Status);
        field.Reason = ex.Message;
        return Fail(report, field.Status, ex.Message);
    }

    private static string BlockingStatus(IEnumerable<ResolutionStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Contains(ResolutionStatus.Ambiguous))
        {
            return "ambiguous";
        }

        if (list.Contains(ResolutionStatus.OptionNotFound))
        {
            return "option-not-found";
        }

        return "not-found";
    }

    private static ExecutionReport Fail(ExecutionReport report, string status, string error)
    {
        report.Success = false;
        report.Status = status;
        report.Error = error;
        return report;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
}
