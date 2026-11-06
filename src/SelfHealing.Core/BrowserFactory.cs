namespace SelfHealing.Core;

/// <summary>
/// Starts Chrome for the runner and the tests. The sandbox flags are on because
/// this is often executed inside a container, where Chrome's sandbox cannot start.
/// A fresh profile is used so a previous run cannot autofill the form.
/// </summary>
public static class BrowserFactory
{
    public static ChromeSession Open(bool headless)
    {
        var profile = Path.Combine(Path.GetTempPath(), "cloudqa-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);

        var chrome = new ChromeOptions();
        if (headless)
        {
            chrome.AddArgument("--headless=new");
        }

        chrome.AddArgument("--no-sandbox");
        chrome.AddArgument("--disable-dev-shm-usage");
        chrome.AddArgument("--disable-gpu");
        chrome.AddArgument("--window-size=1400,1000");
        chrome.AddArgument("--no-first-run");
        chrome.AddArgument("--no-default-browser-check");
        chrome.AddArgument("--disable-extensions");
        chrome.AddArgument("--user-data-dir=" + profile);
        chrome.AddUserProfilePreference("credentials_enable_service", false);
        chrome.AddUserProfilePreference("profile.password_manager_enabled", false);

        // The practice page pulls in iframes and CDN scripts. Eager returns once
        // the document is parsed, which is when the main form exists, instead of
        // waiting for every iframe.
        chrome.PageLoadStrategy = PageLoadStrategy.Eager;

        var driver = new ChromeDriver(chrome);
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        return new ChromeSession(driver, profile);
    }
}

public sealed class ChromeSession : IDisposable
{
    private readonly string _profile;

    public ChromeSession(IWebDriver driver, string profile)
    {
        Driver = driver;
        _profile = profile;
    }

    public IWebDriver Driver { get; }

    public void Dispose()
    {
        try
        {
            Driver.Quit();
        }
        catch (WebDriverException)
        {
            // The browser may already have exited.
        }

        Driver.Dispose();
        try
        {
            if (Directory.Exists(_profile))
            {
                Directory.Delete(_profile, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover profile is harmless.
        }
    }
}
