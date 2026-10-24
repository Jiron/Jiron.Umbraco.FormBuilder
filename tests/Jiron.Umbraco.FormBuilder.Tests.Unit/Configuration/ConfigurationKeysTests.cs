using Jiron.Umbraco.FormBuilder.Core.Configuration;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// The walk that finds the keys an options class does not have (ADR-0042), over shapes the host options will grow into:
/// dictionaries, which take any name, arrays and lists, which take indexes, and the values the binder reads from one string.
/// </summary>
[TestFixture]
public sealed class ConfigurationKeysTests
{
    [Test]
    public void KnownKeys_PassAtEveryDepth()
    {
        Assert.That(
            Failures(new Dictionary<string, string?>
            {
                ["S:Map:anyName:Name"] = "a",
                ["S:Secrets:CrmToken"] = "b",
                ["S:Array:0:Name"] = "c",
                ["S:List:0:Name"] = "d",
                ["S:Inner:Name"] = "e",
                ["S:Number"] = "1",
            }),
            Is.Empty);
    }

    [TestCase("S:Map:anyName:Nam", "S:Map:anyName:Nam is not a setting of Jiron Form Builder; the settings in S:Map:anyName are Name.")]
    [TestCase("S:Array:0:Nam", "S:Array:0:Nam is not a setting of Jiron Form Builder; the settings in S:Array:0 are Name.")]
    [TestCase("S:List:3:Nam", "S:List:3:Nam is not a setting of Jiron Form Builder; the settings in S:List:3 are Name.")]
    [TestCase("S:Secrets:CrmToken:Part", "S:Secrets:CrmToken is a single value, not a section.")]
    [TestCase("S:Empty:Anything", "S:Empty:Anything is not a setting of Jiron Form Builder; the settings in S:Empty are none.")]
    [TestCase("S:Array", "S:Array is a list: give its items as S:Array:0, S:Array:1 and so on.")]
    [TestCase("S:Inner", "S:Inner is a section of settings, not a single value.")]
    public void UnknownKeysAndWrongShapes_AreRefused(string key, string failure)
    {
        Assert.That(Failures(new Dictionary<string, string?> { [key] = "x" }), Is.EqualTo(new[] { failure }));
    }

    [TestCase(nameof(Sample.Number))]
    [TestCase(nameof(Sample.Flag))]
    [TestCase(nameof(Sample.Amount))]
    [TestCase(nameof(Sample.Delay))]
    [TestCase(nameof(Sample.When))]
    [TestCase(nameof(Sample.Moment))]
    [TestCase(nameof(Sample.Id))]
    [TestCase(nameof(Sample.Address))]
    [TestCase(nameof(Sample.Mode))]
    [TestCase(nameof(Sample.Text))]
    public void AValueTheBinderReadsFromOneString_HasNoKeysBelowIt(string property)
    {
        Assert.That(
            Failures(new Dictionary<string, string?> { [$"S:{property}:Below"] = "x" }),
            Is.EqualTo(new[] { $"S:{property} is a single value, not a section." }));
    }

    [Test]
    public void TheSettingsOfAClass_AreItsPublicReadableProperties_WithoutIndexers()
    {
        Assert.That(
            Failures(new Dictionary<string, string?> { ["S:Inner:Hidden"] = "x" }),
            Is.EqualTo(new[] { "S:Inner:Hidden is not a setting of Jiron Form Builder; the settings in S:Inner are Name." }));
    }

    [Test]
    public void TheListOfSettings_ReadsAsASentence()
    {
        Assert.That(
            Failures(new Dictionary<string, string?> { ["P:Third"] = "x" }, typeof(Pair)),
            Is.EqualTo(new[] { "P:Third is not a setting of Jiron Form Builder; the settings in P are First and Second." }));
    }

    private static List<string> Failures(Dictionary<string, string?> settings, Type? type = null)
    {
        var failures = new List<string>();
        var name = type == typeof(Pair) ? "P" : "S";
        ConfigurationKeys.Check(HostOptions.Configuration(settings).GetSection(name), type ?? typeof(Sample), failures);
        return failures;
    }

    private enum Mode
    {
        None,
    }

    private sealed class Sample
    {
        public Dictionary<string, Inner> Map { get; } = [];

        public IDictionary<string, string> Secrets { get; } = new Dictionary<string, string>();

        public Inner[] Array { get; set; } = [];

        public IList<Inner> List { get; } = [];

        public Inner Inner { get; } = new();

        public EmptyClass Empty { get; } = new();

        public int? Number { get; set; }

        public bool Flag { get; set; }

        public decimal Amount { get; set; }

        public TimeSpan Delay { get; set; }

        public DateTime When { get; set; }

        public DateTimeOffset Moment { get; set; }

        public Guid Id { get; set; }

        public Uri? Address { get; set; }

        public Mode Mode { get; set; }

        public string? Text { get; set; }
    }

    private sealed class Inner
    {
        public string? Name { get; set; }

        // Neither is a setting: a property that cannot be read from outside, and an indexer.
        public string Hidden { private get; set; } = string.Empty;

        public string this[int index] => Hidden + index;
    }

    private sealed class EmptyClass;

    private sealed class Pair
    {
        public string? First { get; set; }

        public string? Second { get; set; }
    }
}
