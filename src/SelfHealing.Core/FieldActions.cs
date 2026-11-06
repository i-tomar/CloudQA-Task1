namespace SelfHealing.Core;

public sealed class AutomationException : Exception
{
    public AutomationException(ResolutionStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    public ResolutionStatus Status { get; }
}

/// <summary>
/// Fills a control that has already been resolved and checks the page agreed.
/// Nothing in here clicks a submit button. State is never chosen by index.
/// </summary>
public static class FieldActions
{
    public static string FillText(IWebDriver driver, IWebElement element, string value)
    {
        try
        {
            element.Click();
        }
        catch (ElementNotInteractableException)
        {
            // The click is only there to focus the box. SendKeys still runs.
        }

        element.Clear();
        element.SendKeys(value);
        var actual = DomHelpers.LiveValue(element);
        if (actual != value)
        {
            throw new AutomationException(
                ResolutionStatus.VerificationFailed,
                $"First Name was set to '{value}' but the control now holds '{actual}' ({DomHelpers.Describe(element)}).");
        }

        return actual;
    }

    public static void ClickRadio(IWebDriver driver, IWebElement radio, string desired)
    {
        if (!radio.Selected)
        {
            Click(driver, radio);
        }

        if (!radio.Selected)
        {
            throw new AutomationException(
                ResolutionStatus.VerificationFailed,
                $"Clicked the '{desired}' radio but it is not selected ({DomHelpers.Describe(radio)}).");
        }

        var value = TextNormalizer.Normalize(radio.GetAttribute("value"));
        var adjacent = TextNormalizer.Normalize(DomHelpers.AdjacentOptionText(radio));
        var target = TextNormalizer.Normalize(desired);
        if (value != target && adjacent != target)
        {
            throw new AutomationException(
                ResolutionStatus.VerificationFailed,
                $"The selected radio is '{radio.GetAttribute("value")}' / '{DomHelpers.AdjacentOptionText(radio)}', not '{desired}'.");
        }
    }

    /// <summary>
    /// Finds the option whose normalised value or visible text equals
    /// <paramref name="desired"/>. This does not change the select. The caller
    /// can refuse the whole run before any field is edited.
    /// </summary>
    public static IWebElement FindOption(IWebElement select, string desired)
    {
        var target = TextNormalizer.Normalize(desired);
        if (target.Length == 0)
        {
            throw new AutomationException(ResolutionStatus.OptionNotFound, "State value is empty.");
        }

        IWebElement? match = null;
        var matchCount = 0;
        foreach (var option in select.FindElements(By.TagName("option")))
        {
            if (!option.Enabled)
            {
                continue;
            }

            var value = option.GetAttribute("value") ?? "";
            var text = option.Text ?? "";
            if (TextNormalizer.Normalize(value) != target && TextNormalizer.Normalize(text) != target)
            {
                continue;
            }

            match = option;
            matchCount++;
        }

        if (match == null)
        {
            throw new AutomationException(
                ResolutionStatus.OptionNotFound,
                $"No option in {DomHelpers.Describe(select)} has text or value '{desired}'.");
        }

        if (matchCount > 1)
        {
            throw new AutomationException(
                ResolutionStatus.Ambiguous,
                $"More than one option matches '{desired}' in {DomHelpers.Describe(select)}.");
        }

        return match;
    }

    /// <summary>
    /// Selects an option that <see cref="FindOption"/> already matched.
    /// <see cref="SelectElement.SelectByIndex"/> is never called: the list on
    /// this form is countries, and the first entry is a placeholder.
    /// </summary>
    public static string ApplyOption(IWebDriver driver, IWebElement select, IWebElement option, string desired)
    {
        var target = TextNormalizer.Normalize(desired);
        var rawValue = option.GetAttribute("value") ?? "";
        var selectElement = new SelectElement(select);
        if (TextNormalizer.Normalize(rawValue) == target && rawValue.Length > 0)
        {
            selectElement.SelectByValue(rawValue);
        }

        if (!SelectionMatches(selectElement, target))
        {
            // Visible text matched, or SelectByValue did not stick. Mark the
            // option element itself. Still not an index.
            ((IJavaScriptExecutor)driver).ExecuteScript(
                "arguments[0].selected = true; arguments[1].dispatchEvent(new Event('change', { bubbles: true }));",
                option,
                select);
        }

        if (!SelectionMatches(selectElement, target))
        {
            var selected = selectElement.SelectedOption;
            throw new AutomationException(
                ResolutionStatus.VerificationFailed,
                $"State selection did not stick. Selected text is '{selected.Text}', value is '{selected.GetAttribute("value")}', wanted '{desired}'.");
        }

        var chosen = selectElement.SelectedOption;
        return string.IsNullOrWhiteSpace(chosen.Text) ? rawValue : chosen.Text.Trim();
    }

    private static bool SelectionMatches(SelectElement selectElement, string target)
    {
        var selected = selectElement.SelectedOption;
        var selectedValue = TextNormalizer.Normalize(selected.GetAttribute("value"));
        var selectedText = TextNormalizer.Normalize(selected.Text);
        return selectedValue == target || selectedText == target;
    }

    private static void Click(IWebDriver driver, IWebElement element)
    {
        try
        {
            element.Click();
        }
        catch (Exception ex) when (ex is ElementClickInterceptedException or ElementNotInteractableException)
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);
        }
    }
}
