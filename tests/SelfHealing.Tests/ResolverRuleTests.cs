using System.Text.RegularExpressions;
using SelfHealing.Core;

namespace SelfHealing.Tests;

[TestFixture]
public class ResolverRuleTests
{
    private static readonly string[] FirstNameSynonyms =
        ["first name", "firstname", "given name", "givenname", "forename", "fname"];

    private static readonly string[] Penalties =
        ["last name", "lastname", "surname", "lname", "family name"];

    [Test]
    public void Normalize_CollapsesPadding()
    {
        Assert.That(TextNormalizer.Normalize("  Afganistan \n"), Is.EqualTo("afganistan"));
        Assert.That(TextNormalizer.Normalize(" State "), Is.EqualTo("state"));
        Assert.That(TextNormalizer.NormalizeAttribute("FirstName"), Is.EqualTo("first name"));
        Assert.That(TextNormalizer.NormalizeAttribute("first_name"), Is.EqualTo("first name"));
        Assert.That(TextNormalizer.NormalizeAttribute("last-name"), Is.EqualTo("last name"));
    }

    [Test]
    public void Synonyms_PreferTheExactCaptionOverALongerOne()
    {
        Assert.That(TextMatcher.Strength("First Name", FirstNameSynonyms), Is.EqualTo(1));
        Assert.That(TextMatcher.Strength("Emergency First Name", FirstNameSynonyms), Is.EqualTo(TextMatcher.PartialStrength));
        Assert.That(TextMatcher.Strength("fname", FirstNameSynonyms), Is.EqualTo(1));
        Assert.That(TextMatcher.Strength("lname", FirstNameSynonyms), Is.EqualTo(0));
        Assert.That(TextMatcher.Strength("Middle Name", FirstNameSynonyms), Is.EqualTo(0));
    }

    [Test]
    public void StateWord_DoesNotMatchInsideAnotherWord()
    {
        var synonyms = new[] { "state", "province" };
        Assert.That(TextMatcher.Strength("State", synonyms), Is.EqualTo(1));
        Assert.That(TextMatcher.Strength("Home State", synonyms), Is.EqualTo(TextMatcher.PartialStrength));
        Assert.That(TextMatcher.Strength("statement", synonyms), Is.EqualTo(0));
        Assert.That(TextMatcher.Strength("United States", synonyms), Is.EqualTo(0));
    }

    [Test]
    public void Penalties_CatchLastNameSpellings()
    {
        Assert.That(TextMatcher.HitsPenalty("Last Name", Penalties), Is.True);
        Assert.That(TextMatcher.HitsPenalty("Surname", Penalties), Is.True);
        Assert.That(TextMatcher.HitsPenalty("lname", Penalties), Is.True);
        Assert.That(TextMatcher.HitsPenalty("last_name", Penalties), Is.True);
        Assert.That(TextMatcher.HitsPenalty("First Name", Penalties), Is.False);
        Assert.That(TextMatcher.HitsPenalty("Name", Penalties), Is.False);
    }

    [TestCase(new[] { 66, 10 }, 14, 12, ResolutionStatus.Resolved)]
    [TestCase(new[] { 60, 60 }, 14, 12, ResolutionStatus.Ambiguous)]
    [TestCase(new[] { 20, 10 }, 14, 12, ResolutionStatus.Ambiguous)]
    [TestCase(new[] { 10 }, 14, 12, ResolutionStatus.NotFound)]
    [TestCase(new int[0], 14, 12, ResolutionStatus.NotFound)]
    public void AmbiguityRule_RequiresThresholdAndMargin(int[] scores, int threshold, int margin, ResolutionStatus expected)
    {
        var status = AmbiguityRule.Decide(scores, threshold, margin, out _);
        Assert.That(status, Is.EqualTo(expected));
    }

    [Test]
    public void Mutator_ChangesIdsClassesAndOrder_AndKeepsTheGenderTrap()
    {
        var original = File.ReadAllText(Path.Combine(RepoLocator.Find(), "test-pages", "original.html"));
        var orderChanged = false;
        for (var seed = 1; seed <= 20 && !orderChanged; seed++)
        {
            var mutated = PageMutator.Mutate(original, seed);
            orderChanged = PageMutator.BlockSignature(mutated) != PageMutator.BlockSignature(original);
            Assert.That(mutated, Does.Not.Contain("id=\"fname\""));
            Assert.That(mutated, Does.Not.Contain("class=\"form-control\""));
            Assert.That(mutated, Does.Contain(">First Name<"));
            Assert.That(mutated, Does.Contain("value=\"Afganistan\""));

            var female = Regex.Match(mutated, "<input\\b[^>]*\\bvalue=\"Female\"[^>]*>", RegexOptions.IgnoreCase);
            var femaleId = Regex.Match(female.Value, "\\bid=\"([^\"]+)\"").Groups[1].Value;
            var trap = Regex.Match(mutated, "<span\\b([^>]*)>\\s*Transgender", RegexOptions.IgnoreCase);
            var trapFor = Regex.Match(trap.Groups[1].Value, "\\bfor=\"([^\"]+)\"").Groups[1].Value;
            Assert.That(trapFor, Is.EqualTo(femaleId));
            Assert.That(trapFor, Is.Not.EqualTo("female"));
        }

        Assert.That(orderChanged, Is.True);
    }
}

[TestFixture]
public class SettingsLoaderTests
{
    [Test]
    public void Url_CommandLineBeatsEnvironmentBeatsJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cloudqa-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), """
            {
              "CloudQa": {
                "Url": "https://json.example/form",
                "FirstName": "FromJson",
                "Gender": "Male",
                "State": "India",
                "Headless": true
              }
            }
            """);

        var previous = Environment.GetEnvironmentVariable("CLOUDQA_URL");
        Environment.SetEnvironmentVariable("CLOUDQA_URL", "https://env.example/form");
        try
        {
            var fromEnv = SettingsLoader.Load([], directory);
            Assert.That(fromEnv.Url, Is.EqualTo("https://env.example/form"));
            Assert.That(fromEnv.FirstName, Is.EqualTo("FromJson"));
            Assert.That(fromEnv.FirstNameSynonyms, Does.Contain("fname"));

            var fromCli = SettingsLoader.Load(["--url", "https://cli.example/form", "--firstname", "Asha"], directory);
            Assert.That(fromCli.Url, Is.EqualTo("https://cli.example/form"));
            Assert.That(fromCli.FirstName, Is.EqualTo("Asha"));
            Assert.That(fromCli.Gender, Is.EqualTo("Male"));

            var positional = SettingsLoader.Load(["https://positional.example/form"], directory);
            Assert.That(positional.Url, Is.EqualTo("https://positional.example/form"));

            var flagWins = SettingsLoader.Load(["https://positional.example/form", "--url", "https://flag.example/form"], directory);
            Assert.That(flagWins.Url, Is.EqualTo("https://flag.example/form"));

            Environment.SetEnvironmentVariable("CLOUDQA_URL", null);
            var fromJson = SettingsLoader.Load([], directory);
            Assert.That(fromJson.Url, Is.EqualTo("https://json.example/form"));
            Assert.That(fromJson.Headless, Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLOUDQA_URL", previous);
            Directory.Delete(directory, recursive: true);
        }
    }
}
