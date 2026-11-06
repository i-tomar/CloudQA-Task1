namespace SelfHealing.Core;

/// <summary>
/// Chooses the form the three fields are resolved inside.
/// Each visible top-level form is scored by whether it contains a text input,
/// a radio, and a select. The highest score wins. A tie goes to the earliest
/// form in the document, which on the practice page is the real form. Later
/// copies live in shadow trees and iframes; the shadow copies that also leak
/// light-DOM controls into their own form tie on score and lose on order.
/// </summary>
public static class FormScoper
{
    public sealed class Choice
    {
        public FormReport? Chosen { get; init; }

        public IWebElement? ChosenElement { get; init; }

        public List<FormReport> Forms { get; init; } = [];

        public int HiddenFormsSkipped { get; init; }

        public int IframesNotEntered { get; init; }

        public int ShadowHostsNotEntered { get; init; }
    }

    public static Choice Choose(IWebDriver driver)
    {
        var forms = driver.FindElements(By.TagName("form"));
        var reports = new List<FormReport>();
        var elements = new List<IWebElement>();
        var hidden = 0;

        for (var i = 0; i < forms.Count; i++)
        {
            var form = forms[i];
            if (!DomHelpers.IsDisplayed(form))
            {
                hidden++;
                continue;
            }

            var hasText = false;
            var hasRadio = false;
            var hasSelect = false;
            foreach (var control in form.FindElements(By.CssSelector("input, textarea, select")))
            {
                if (!DomHelpers.IsInteractable(control))
                {
                    continue;
                }

                if (DomHelpers.IsTextLike(control))
                {
                    hasText = true;
                }
                else if (DomHelpers.IsRadio(control))
                {
                    hasRadio = true;
                }
                else if (DomHelpers.IsSelect(control))
                {
                    hasSelect = true;
                }
            }

            var score = (hasText ? 1 : 0) + (hasRadio ? 1 : 0) + (hasSelect ? 1 : 0);
            reports.Add(new FormReport
            {
                Index = i,
                Score = score,
                Id = form.GetAttribute("id"),
                Name = form.GetAttribute("name"),
                ClassName = form.GetAttribute("class"),
                HasTextInput = hasText,
                HasRadio = hasRadio,
                HasSelect = hasSelect,
            });
            elements.Add(form);
        }

        FormReport? chosenReport = null;
        IWebElement? chosenElement = null;
        if (reports.Count > 0)
        {
            var best = reports.Max(report => report.Score);
            var winnerIndex = reports.FindIndex(report => report.Score == best);
            chosenReport = reports[winnerIndex];
            chosenReport.Chosen = true;
            chosenElement = elements[winnerIndex];
        }

        return new Choice
        {
            Chosen = chosenReport,
            ChosenElement = chosenElement,
            Forms = reports,
            HiddenFormsSkipped = hidden,
            IframesNotEntered = driver.FindElements(By.TagName("iframe")).Count,
            ShadowHostsNotEntered = CountShadowHosts(driver),
        };
    }

    private static int CountShadowHosts(IWebDriver driver)
    {
        try
        {
            var raw = ((IJavaScriptExecutor)driver).ExecuteScript(
                "return Array.from(document.querySelectorAll('*')).filter(function (el) { return !!el.shadowRoot; }).length;");
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        catch (WebDriverException)
        {
            return 0;
        }
    }
}
