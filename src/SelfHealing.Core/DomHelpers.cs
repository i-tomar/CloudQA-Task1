namespace SelfHealing.Core;

/// <summary>A short piece of text found near a control, and what kind of node it came from.</summary>
public sealed record TextSource(string Kind, string Text);

/// <summary>
/// DOM reads used by the scorer. Locators here are relative (parent, sibling,
/// a label whose for equals this element's id). They are not fixed paths to a
/// field on the practice form.
/// </summary>
public static class DomHelpers
{
    private static readonly HashSet<string> TextInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "text", "search", "email", "tel", "url",
    };

    public static bool IsDisplayed(IWebElement element)
    {
        try
        {
            return element.Displayed;
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    }

    /// <summary>
    /// Hard filter: on screen, enabled, not read-only, not aria-disabled.
    /// A read-only box reports Enabled in Selenium, so it is checked separately.
    /// </summary>
    public static bool IsInteractable(IWebElement element)
    {
        try
        {
            if (!element.Displayed || !element.Enabled)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(element.GetAttribute("readonly")))
            {
                return false;
            }

            var ariaDisabled = element.GetAttribute("aria-disabled");
            return !string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    }

    public static bool IsTextLike(IWebElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        if (tag == "textarea")
        {
            return true;
        }

        if (tag != "input")
        {
            return false;
        }

        // Number, date, password, file and hidden inputs are a different kind of
        // field. Mobile (type=number) and the file picker cannot be a first name.
        var type = (element.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
        return TextInputTypes.Contains(type);
    }

    public static bool IsRadio(IWebElement element)
    {
        return element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.GetAttribute("type"), "radio", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSelect(IWebElement element)
    {
        return element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase);
    }

    public static string Describe(IWebElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        var type = element.GetAttribute("type");
        var id = element.GetAttribute("id");
        var name = element.GetAttribute("name");
        var placeholder = element.GetAttribute("placeholder");
        var parts = new List<string> { tag };
        if (tag == "input" && !string.IsNullOrEmpty(type))
        {
            parts.Add($"type={type}");
        }

        if (!string.IsNullOrEmpty(id))
        {
            parts.Add($"id={id}");
        }

        if (!string.IsNullOrEmpty(name))
        {
            parts.Add($"name={name}");
        }

        if (!string.IsNullOrEmpty(placeholder))
        {
            parts.Add($"placeholder={placeholder}");
        }

        return string.Join(" ", parts);
    }

    public static string LiveValue(IWebElement element)
    {
        try
        {
            return element.GetDomProperty("value") ?? "";
        }
        catch (WebDriverException)
        {
            return element.GetAttribute("value") ?? "";
        }
    }

    /// <summary>
    /// Labels wired to the control with for= or by wrapping it.
    /// Only a label element counts. The practice form puts for= on a span next
    /// to the Transgender radio (and points that for at "female"). That span is
    /// ignored here on purpose.
    /// </summary>
    public static IReadOnlyList<TextSource> LinkedLabels(IWebElement element, IWebElement form)
    {
        var found = new List<TextSource>();
        var wrapping = element.FindElements(By.XPath("ancestor::label[1]")).FirstOrDefault();
        AddShortText(found, wrapping, SignalNames.Label);

        var id = element.GetAttribute("id");
        if (string.IsNullOrEmpty(id))
        {
            return found;
        }

        foreach (var label in form.FindElements(By.TagName("label")))
        {
            if (label.GetAttribute("for") == id)
            {
                AddShortText(found, label, SignalNames.Label);
            }
        }

        return found;
    }

    public static IReadOnlyList<TextSource> AccessibleNames(IWebElement element, IWebElement searchRoot)
    {
        var found = new List<TextSource>();
        var aria = element.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(aria))
        {
            found.Add(new TextSource(SignalNames.AccessibleName, aria.Trim()));
        }

        var title = element.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            found.Add(new TextSource(SignalNames.AccessibleName, title.Trim()));
        }

        var labelledBy = element.GetAttribute("aria-labelledby");
        if (string.IsNullOrWhiteSpace(labelledBy))
        {
            return found;
        }

        foreach (var id in labelledBy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var node = searchRoot
                .FindElements(By.XPath(".//*[@id=" + XPathLiteral(id) + "]"))
                .FirstOrDefault();
            AddShortText(found, node, SignalNames.AccessibleName);
        }

        return found;
    }

    /// <summary>
    /// Text that sits immediately beside the control, then beside its containers,
    /// stopping at the form. A container that holds another control is not read,
    /// so a neighbour field's caption cannot leak in.
    /// A real label or legend is returned as a label signal. A span, p or div is
    /// nearby text. The for attribute of a span is never read.
    /// </summary>
    public static IReadOnlyList<TextSource> Nearby(IWebElement element)
    {
        var found = new List<TextSource>();
        var current = element;
        for (var depth = 0; depth < 4; depth++)
        {
            ConsiderSibling(found, current, "preceding-sibling::*[1]");
            if (depth == 0)
            {
                ConsiderSibling(found, current, "following-sibling::*[1]");
            }

            var parent = current.FindElements(By.XPath("parent::*")).FirstOrDefault();
            if (parent == null || IsFormOrDocument(parent))
            {
                break;
            }

            current = parent;
        }

        var fieldset = element.FindElements(By.XPath("ancestor::fieldset[1]")).FirstOrDefault();
        if (fieldset != null)
        {
            AddShortText(found, fieldset.FindElements(By.TagName("legend")).FirstOrDefault(), SignalNames.Label);
            var aria = fieldset.GetAttribute("aria-label");
            if (!string.IsNullOrWhiteSpace(aria))
            {
                found.Add(new TextSource(SignalNames.AccessibleName, aria.Trim()));
            }
        }

        return found;
    }

    /// <summary>
    /// The visible caption of a radio option: the next element's text.
    /// The span beside Transgender says for="female"; that attribute is not read.
    /// </summary>
    public static string? AdjacentOptionText(IWebElement radio)
    {
        var next = radio.FindElements(By.XPath("following-sibling::*[1]")).FirstOrDefault();
        if (next == null || IsControl(next) || ContainsControl(next))
        {
            return null;
        }

        var text = Collapse(next.Text);
        return text.Length is > 0 and <= 80 ? text : null;
    }

    public static string XPathLiteral(string value)
    {
        if (!value.Contains('\''))
        {
            return "'" + value + "'";
        }

        if (!value.Contains('"'))
        {
            return "\"" + value + "\"";
        }

        var pieces = value.Split('\'');
        return "concat('" + string.Join("', \"'\", '", pieces) + "')";
    }

    public static int Scale(int weight, double strength)
    {
        if (weight <= 0 || strength <= 0)
        {
            return 0;
        }

        return (int)Math.Round(weight * strength, MidpointRounding.AwayFromZero);
    }

    private static void ConsiderSibling(List<TextSource> found, IWebElement element, string axis)
    {
        var sibling = element.FindElements(By.XPath(axis)).FirstOrDefault();
        if (sibling == null || IsControl(sibling) || ContainsControl(sibling))
        {
            return;
        }

        var kind = sibling.TagName.ToLowerInvariant() is "label" or "legend"
            ? SignalNames.Label
            : SignalNames.NearbyText;
        AddShortText(found, sibling, kind);
    }

    private static void AddShortText(List<TextSource> found, IWebElement? element, string kind)
    {
        if (element == null)
        {
            return;
        }

        var text = Collapse(element.Text);
        if (text.Length is > 0 and <= 80)
        {
            found.Add(new TextSource(kind, text));
        }
    }

    private static bool ContainsControl(IWebElement element)
    {
        try
        {
            return element.FindElements(By.CssSelector("input, select, textarea, button")).Count > 0;
        }
        catch (StaleElementReferenceException)
        {
            return true;
        }
    }

    private static bool IsControl(IWebElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        return tag is "input" or "select" or "textarea" or "button";
    }

    private static bool IsFormOrDocument(IWebElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        return tag is "form" or "body" or "html";
    }

    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        return Regex.Replace(text.Replace('\u00A0', ' ').Trim(), @"\s+", " ");
    }
}
