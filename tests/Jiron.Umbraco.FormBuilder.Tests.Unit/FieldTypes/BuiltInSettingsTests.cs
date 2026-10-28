using System.Globalization;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The rules and own checks of the built-in field types (ADR-0056): the rules of their attributes restated apart from
/// the code and held at their edges, and each type's own check refusing what the rules cannot say, at its path, and
/// accepting what lies just inside it.
/// </summary>
public sealed class BuiltInSettingsTests
{
    private const string FieldPath = "$.pages[0].groups[0].fields[0]";

    // The rules of each built-in type's settings, those that differ from what a setting without attributes keeps: a
    // range, characters (in each culture), items, required.
    private static readonly Dictionary<string, string> Declared = new(StringComparer.Ordinal)
    {
        ["text"] = "minLength 0..4000; maxLength 1..4000; prefix chars<=50; suffix chars<=50",
        ["textarea"] = "rows 1..50; minLength 0..100000; maxLength 1..100000; minWords 1..20000; maxWords 1..20000",
        ["email"] = "confirmLabel chars<=200; domains.list chars<=253",
        ["number"] = "decimals 0..15; prefix chars<=50; suffix chars<=50",
        ["time"] = "stepMinutes 1..720",
        ["datetime"] = "stepMinutes 1..720",
        ["rating"] = "count 3..10; lowLabel chars<=100; highLabel chars<=100",
        ["scale"] = "from 0..1; to 5..10; lowLabel chars<=100; highLabel chars<=100",
        ["slider"] = "unit chars<=50",
        ["calculation"] = "formula required chars<=1000; format.decimals 0..15; format.prefix chars<=50; format.suffix chars<=50",
        ["dropdown"] = "prompt chars<=200; minSelections 1..1000; maxSelections 1..1000",
        ["radio"] = "otherLabel chars<=200",
        ["checkboxes"] = "minSelections 1..1000; maxSelections 1..1000; otherLabel chars<=200",
        ["grid"] = "scale.from 0..1; scale.to 2..10",
        ["ranking"] = "top 1..1000",
        ["checkbox"] = "text chars<=20000",
        ["yesno"] = "yesLabel chars<=100; noLabel chars<=100",
        ["consent"] = "text required chars<=20000",
        ["file"] = "extensions chars<=10; maxBytes 1..999999999999999; maxFiles 1..20",
        ["signature"] = "width 200..1200; height 100..600",
        ["repeater"] = "minRows 0..100; maxRows 1..100; addLabel chars<=100; removeLabel chars<=100",
        ["heading"] = "text required chars<=300; level 2..4",
        ["paragraph"] = "html required",
        ["image"] = "media required; alt required chars<=300",
    };

    public static IEnumerable<string> Types() => TestFieldTypes.Registry.All.Select(type => type.Alias);

    public static IEnumerable<TestCaseData> OwnChecks()
    {
        static TestCaseData Refuses(string name, string type, string settings, params string[] paths) =>
            new TestCaseData(type, settings, null, paths).SetName($"Refuses({name})");
        static TestCaseData Accepts(string name, string type, string settings) =>
            new TestCaseData(type, settings, null, Array.Empty<string>()).SetName($"Accepts({name})");
        static TestCaseData WithField(TestCaseData data, Action<JsonObject> change) =>
            new TestCaseData(data.Arguments[0], data.Arguments[1], change, data.Arguments[3]).SetName(data.TestName!);
        static Action<JsonObject> Options(int count) => field => field["options"] = new JsonObject
        {
            ["items"] = new JsonArray([.. Enumerable.Range(1, count).Select(i => (JsonNode)new JsonObject { ["value"] = $"o{i}" })]),
        };
        var umlaut = $"b{(char)0xFC}cher.de";

        yield return Refuses("a text's fewest characters above its most", "text", """{"minLength":10,"maxLength":5}""", "minLength");
        yield return Accepts("a text's fewest characters at its most", "text", """{"minLength":5,"maxLength":5}""");
        yield return Refuses("a long text's fewest characters above its most", "textarea", """{"minLength":10,"maxLength":5}""", "minLength");
        yield return Refuses("a long text's fewest words above its most", "textarea", """{"minWords":10,"maxWords":5}""", "minWords");
        yield return Accepts("a long text's fewest words at its most", "textarea", """{"minWords":5,"maxWords":5}""");

        yield return Refuses("a domain in capitals", "email", """{"domains":{"mode":"allow","list":["Example.com"]}}""", "domains.list[0]");
        yield return Refuses("a domain of one label", "email", """{"domains":{"mode":"allow","list":["localhost"]}}""", "domains.list[0]");
        yield return Refuses("a domain with a space", "email", """{"domains":{"mode":"block","list":["example.com","exa mple.com"]}}""", "domains.list[1]");
        yield return Refuses("a domain with an empty label", "email", """{"domains":{"mode":"block","list":["example..com"]}}""", "domains.list[0]");
        yield return Refuses("a domain ending in a point", "email", """{"domains":{"mode":"block","list":["example.com."]}}""", "domains.list[0]");
        yield return Refuses("domains listed for any domain", "email", """{"domains":{"list":["example.com"]}}""", "domains.list");
        yield return Refuses("domains allowed without one", "email", """{"domains":{"mode":"allow"}}""", "domains.list");
        yield return Accepts("domains allowed, an internationalised one among them", "email", $$$"""{"domains":{"mode":"allow","list":["example.com","{{{umlaut}}}","xn--bcher-kva.ch"]}}""");

        yield return Refuses("a phone's country in lowercase", "phone", """{"defaultCountry":"ch"}""", "defaultCountry");
        yield return Refuses("a phone's country that is none", "phone", """{"defaultCountry":"XX"}""", "defaultCountry");
        yield return Accepts("a phone's country", "phone", """{"defaultCountry":"XK"}""");

        yield return Refuses("a number's minimum above its maximum", "number", """{"min":"10","max":"5"}""", "min");
        yield return Refuses("a number's step of zero", "number", """{"step":"0"}""", "step");
        yield return Refuses("a number's step below zero", "number", """{"step":"-1"}""", "step");
        yield return Refuses("a number's minimum with more places than its decimals", "number", """{"min":"0.5"}""", "min");
        yield return Refuses("a number's step with more places than its decimals", "number", """{"decimals":1,"step":"0.25"}""", "step");
        yield return Refuses("a number's maximum with more places than its decimals", "number", """{"decimals":1,"max":"1.25"}""", "max");
        yield return Accepts("a number's limits within its decimals", "number", """{"decimals":2,"max":"10.25","min":"0.5","step":"0.25"}""");
        yield return Accepts("a number's limit with a trailing zero", "number", """{"decimals":1,"min":"1.50"}""");

        yield return Refuses("a date's earliest after its latest", "date", """{"max":"2026-01-01","min":"2026-12-31"}""", "min");
        yield return Refuses("a date's earliest a day after its latest", "date", """{"max":"2026-01-01","min":"2026-01-02"}""", "min");
        yield return Refuses("a date's earliest in another form", "date", """{"min":"31.12.2026"}""", "min");
        yield return Refuses("a date's earliest as a number", "date", """{"min":5}""", "min");
        yield return Refuses("a date's latest relative in another unit", "date", """{"max":{"today":"+2x"}}""", "max");
        yield return Refuses("a date's latest relative with more", "date", """{"max":{"at":"noon","today":"+2d"}}""", "max");
        yield return Refuses("a date's latest relative as a number", "date", """{"max":{"today":2}}""", "max");
        yield return Accepts("a date's limits, one relative", "date", """{"max":"2030-01-01","min":{"today":"-1y"}}""");
        yield return Accepts("a date's earliest today", "date", """{"min":{"today":""}}""");
        yield return Accepts("a date's earliest at its latest", "date", """{"max":"2026-01-01","min":"2026-01-01"}""");
        yield return Refuses("a time's earliest after its latest", "time", """{"max":"09:00","min":"18:00"}""", "min");
        yield return Refuses("a time's earliest a second after its latest", "time", """{"max":"09:00","min":"09:00:01"}""", "min");
        yield return Accepts("a time's earliest at its latest", "time", """{"max":"09:00","min":"09:00"}""");
        yield return Refuses("a moment's earliest after its latest", "datetime", """{"max":"2026-01-01","min":"2026-12-31"}""", "min");
        yield return Refuses("a moment's latest in words", "datetime", """{"max":"tomorrow"}""", "max");
        yield return Accepts("a moment's earliest relative", "datetime", """{"min":{"today":"+1d"}}""");

        yield return Refuses("a slider's minimum at its maximum", "slider", """{"max":"10","min":"10"}""", "min");
        yield return Refuses("a slider's minimum above its maximum", "slider", """{"max":"5","min":"10"}""", "min");
        yield return Refuses("a slider's step of zero", "slider", """{"step":"0"}""", "step");
        yield return Refuses("a slider's step beyond its span", "slider", """{"max":"1","min":"0","step":"2"}""", "step");
        yield return Accepts("a slider's step of its span", "slider", """{"max":"1","min":"0","step":"1"}""");
        yield return Accepts("a slider below zero, in quarters", "slider", """{"max":"0.5","min":"-0.5","step":"0.25"}""");

        yield return Refuses("a dropdown's fewest choices without several", "dropdown", """{"minSelections":1}""", "minSelections");
        yield return Refuses("a dropdown's most choices without several", "dropdown", """{"maxSelections":2}""", "maxSelections");
        yield return WithField(Refuses("a dropdown's fewest choices above its most", "dropdown", """{"maxSelections":2,"minSelections":3,"multiple":true}""", "minSelections"), Options(5));
        yield return Refuses("a dropdown's fewest choices above its options", "dropdown", """{"minSelections":2,"multiple":true}""", "minSelections");
        yield return Accepts("a dropdown's choices within its options", "dropdown", """{"maxSelections":5,"minSelections":1,"multiple":true}""");
        yield return WithField(Refuses("checkboxes' fewest above their most", "checkboxes", """{"maxSelections":2,"minSelections":3}""", "minSelections"), Options(5));
        yield return Refuses("checkboxes' fewest above their options", "checkboxes", """{"minSelections":2}""", "minSelections");
        yield return Accepts("checkboxes' fewest with the Other option", "checkboxes", """{"minSelections":2,"other":true}""");
        yield return Refuses("checkboxes' Other label without Other", "checkboxes", """{"otherLabel":{"":"Something else"}}""", "otherLabel");
        yield return Accepts("checkboxes' Other with its label", "checkboxes", """{"other":true,"otherLabel":{"":"Something else"}}""");
        yield return Refuses("radio buttons' Other label without Other", "radio", """{"otherLabel":{"":"Something else"}}""", "otherLabel");
        yield return Accepts("radio buttons' Other with the type's label", "radio", """{"other":true}""");
        yield return Refuses("a grid with options and a scale", "grid", """{"scale":{"from":1,"to":5}}""", "scale");
        yield return WithField(Refuses("a grid with neither options nor a scale", "grid", "{}", "scale"), field => field.Remove("options"));
        yield return WithField(Accepts("a grid with a scale", "grid", """{"scale":{"from":0,"to":10}}"""), field => field.Remove("options"));
        yield return Refuses("a ranking of more options than it has", "ranking", """{"top":2}""", "top");
        yield return Accepts("a ranking of its options", "ranking", """{"top":1}""");

        yield return Refuses("a country allowed in lowercase", "country", """{"allowed":["CH","ch"]}""", "allowed[1]");
        yield return Refuses("a country first that is not allowed", "country", """{"allowed":["CH"],"top":["DE"]}""", "top[0]");
        yield return Refuses("a country at the start that is not allowed", "country", """{"allowed":["CH"],"default":"DE"}""", "default");
        yield return Refuses("a country at the start that is none", "country", """{"default":"EU"}""", "default");
        yield return Accepts("countries allowed, first and at the start", "country", """{"allowed":["CH","DE"],"default":"DE","top":["CH"]}""");
        yield return Accepts("countries first, all allowed", "country", """{"default":"CH","top":["XK"]}""");

        yield return Refuses("a name's required part it does not ask for", "name", """{"requiredParts":["middle"]}""", "requiredParts[0]");
        yield return Accepts("a name's required part it asks for", "name", """{"parts":["first","middle","last"],"requiredParts":["middle"]}""");
        yield return Refuses("an address part that is always asked for", "address", """{"parts":["street"]}""", "parts[0]");
        yield return Refuses("an address's required part it does not ask for", "address", """{"requiredParts":["region"]}""", "requiredParts[0]");
        yield return Accepts("an address's required parts it asks for", "address", """{"parts":["region"],"requiredParts":["street","region"]}""");
        yield return Refuses("a file extension with its point", "file", """{"extensions":[".dwg"]}""", "extensions[0]");
        yield return Refuses("a file extension in capitals", "file", """{"extensions":["dwg","DXF"]}""", "extensions[1]");
        yield return Accepts("file extensions", "file", """{"extensions":["dwg","3ds"]}""");
        yield return Refuses("a file extension the site blocks", "file", """{"extensions":["dwg","exe"]}""", "extensions[1]");
        yield return Refuses("a file's most bytes above the site's", "file", """{"maxBytes":52428801}""", "maxBytes");
        yield return Accepts("a file's most bytes at the site's", "file", """{"maxBytes":52428800}""");
        yield return Refuses("a repeatable group's fewest rows above its most", "repeater", """{"maxRows":2,"minRows":5}""", "minRows");
        yield return Accepts("a repeatable group's fewest rows at its most", "repeater", """{"maxRows":2,"minRows":2}""");
    }

    [TestCaseSource(nameof(Types))]
    public void TheRulesOfEachType_AreTheOnesDeclared(string type)
    {
        var declared = Declared.TryGetValue(type, out var rules) ? rules.Split("; ") : [];

        Assert.That(Rules(TestFieldTypes.Registry.FindType(type)!.SettingsSchema.Settings, string.Empty), Is.EquivalentTo(declared));
    }

    [TestCaseSource(nameof(Types))]
    public void EachRuleOfEachType_HoldsAtItsEdges(string type)
    {
        var schema = TestFieldTypes.Registry.FindType(type)!.SettingsSchema;
        Assert.Multiple(() =>
        {
            foreach (var (path, setting) in Walk(schema.Settings, string.Empty))
            {
                var rules = setting.Rules;
                var kind = setting.ItemKind ?? setting.Kind;
                if (kind == FieldSettingKind.WholeNumber && rules.Minimum is { } lowest && rules.Maximum is { } highest && lowest != IntMinimum)
                {
                    var (low, high) = (long.Parse(lowest, CultureInfo.InvariantCulture), long.Parse(highest, CultureInfo.InvariantCulture));
                    Holds(schema, path, setting, JsonValue.Create(low), null);
                    Holds(schema, path, setting, JsonValue.Create(high), null);
                    Holds(schema, path, setting, JsonValue.Create(low - 1), DefinitionErrors.SettingInvalid);

                    // Settings store at most 15 digits, so a bound of 15 nines is kept by every stored number.
                    if (high < 999_999_999_999_999)
                    {
                        Holds(schema, path, setting, JsonValue.Create(high + 1), DefinitionErrors.SettingInvalid);
                    }
                }

                if (kind is FieldSettingKind.Text or FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml && rules.MaximumLength is { } most)
                {
                    Holds(schema, path, setting, Text(kind, new string('a', most)), null);
                    Holds(schema, path, setting, Text(kind, new string('a', most + 1)), DefinitionErrors.SettingInvalid);
                }

                if (rules.Required)
                {
                    Assert.That(Check(schema, null), Has.Member($"{DefinitionErrors.SettingMissing} at $.{path}"), $"{type}: {path} is required");
                }
            }
        });
    }

    [TestCaseSource(nameof(OwnChecks))]
    public void EachTypesOwnCheck_RefusesWhatTheRulesCannotSay(string type, string settings, Action<JsonObject>? change, string[] paths)
    {
        var field = Field(type, JsonNode.Parse(settings)!.AsObject());
        change?.Invoke(field);

        Assert.That(Publish(field), Is.EqualTo(paths.Select(path => $"{DefinitionErrors.SettingInvalid} at {FieldPath}.settings.{path}")));
    }

    [Test]
    public void TheOwnChecks_SayWhatIsWrong()
    {
        string MessageOf(string type, string settings) =>
            StructureTests.Publish(Document(Field(type, JsonNode.Parse(settings)!.AsObject()))).Single().Message;

        Assert.Multiple(() =>
        {
            Assert.That(MessageOf("text", """{"minLength":10,"maxLength":5}"""), Is.EqualTo("The setting 'minLength' is 10, more than 'maxLength', 5."));
            Assert.That(MessageOf("number", """{"decimals":1,"step":"0.25"}"""), Is.EqualTo("The setting 'step', 0.25, has more places after the point than 'decimals', 1, allows."));
            Assert.That(MessageOf("slider", """{"max":"1","min":"0","step":"2"}"""), Is.EqualTo("The setting 'step' is at most the distance from 'min' to 'max', 1, not 2."));
            Assert.That(MessageOf("email", """{"domains":{"mode":"allow"}}"""), Is.EqualTo("An email field that allows or blocks domains lists at least one."));
            Assert.That(MessageOf("country", """{"default":"EU"}"""), Is.EqualTo("A country is its two-letter code in capitals, such as CH, not 'EU'."));
            Assert.That(MessageOf("dropdown", """{"minSelections":2,"multiple":true}"""), Is.EqualTo("The setting 'minSelections' is 2, but the field has 1 choice."));
            Assert.That(MessageOf("name", """{"requiredParts":["middle"]}"""), Is.EqualTo("The part 'middle' is required, but 'parts' does not ask for it."));
            Assert.That(MessageOf("date", """{"min":"31.12.2026"}"""), Is.EqualTo("The setting 'min' is a date such as \"2026-12-31\", or a date relative to today such as { \"today\": \"+2d\" }."));
            Assert.That(MessageOf("file", """{"extensions":["svg"]}"""), Is.EqualTo("This site blocks the extension 'svg' for every field (Files:BlockedExtensions)."));
            Assert.That(MessageOf("file", """{"maxBytes":52428801}"""), Is.EqualTo("A file on this site has at most 52,428,800 bytes (Files:MaxFileBytes)."));
        });
    }

    [Test]
    public void AFileField_IsHeldToTheSitesOwnLimits()
    {
        var site = new FieldHostLimits(1000, ["dwg"]);
        List<string> PublishOn(string settings) =>
        [
            .. new StructuralPublishCheck(DefinitionFixtures.Serializer(), TestFieldTypes.Registry, site)
                .Check(DefinitionFixtures.Read(Document(Field("file", JsonNode.Parse(settings)!.AsObject()))))
                .Select(problem => $"{problem.Code} at {problem.Path}"),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(PublishOn("""{"extensions":["svg","exe"],"maxBytes":1000}"""), Is.Empty, "the site's own list replaces the defaults");
            Assert.That(PublishOn("""{"extensions":["svg","dwg"]}"""), Is.EqualTo(new[] { $"{DefinitionErrors.SettingInvalid} at {FieldPath}.settings.extensions[1]" }));
            Assert.That(PublishOn("""{"maxBytes":1001}"""), Is.EqualTo(new[] { $"{DefinitionErrors.SettingInvalid} at {FieldPath}.settings.maxBytes" }));
        });
    }

    [Test]
    public void OnlyAListThatIsNeverEmpty_StartsWithItems()
    {
        // Settings leave an empty list out, so a list that starts with items could never be emptied. Every other list
        // starts empty and says what empty means: a file field without types takes images and PDFs.
        var startsWithItems = TestFieldTypes.Registry.Types
            .SelectMany(type => Walk(type.SettingsSchema.Settings, type.Declaration.Alias + "."))
            .Where(each => each.Setting.Kind == FieldSettingKind.List && each.Setting.Default is not null)
            .Select(each => each.Path);

        Assert.That(startsWithItems, Is.EquivalentTo(new[] { "url.schemes", "name.parts" }), "a web address needs a scheme, a name a part");
    }

    [Test]
    public void TheCountryCodes_AreThoseOfIso3166_WithKosovo()
    {
        // The exceptionally reserved codes some cultures carry, which are not countries a visitor lives in.
        string[] reserved = ["AC", "CP", "CQ", "DG", "EA", "EU", "EZ", "FX", "IC", "SU", "TA", "UK", "UN"];
        var regions = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => RegionOf(culture.Name))
            .OfType<string>()
            .Where(region => region.Length == 2 && region.All(char.IsAsciiLetterUpper))
            .Except(reserved)
            .Distinct();

        Assert.Multiple(() =>
        {
            Assert.That(BuiltInChecks.CountryCodes, Has.Count.EqualTo(250), "249 countries and territories, and Kosovo");
            Assert.That(BuiltInChecks.CountryCodes.Where(code => RegionOf(code) != code), Is.EqualTo(new[] { "EH" }), ".NET knows each but Western Sahara, which has no culture");
            Assert.That(regions.Where(region => !BuiltInChecks.CountryCodes.Contains(region)), Is.Empty, "the region of every culture .NET has");
        });
    }

    private static readonly string IntMinimum = int.MinValue.ToString(CultureInfo.InvariantCulture);

    private static string? RegionOf(string name)
    {
        try
        {
            return new RegionInfo(name).TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Each rule in the words of the table above, by the setting's place in the settings.
    private static IEnumerable<string> Rules(IEnumerable<FieldSettingDescriptor> settings, string prefix)
    {
        foreach (var (path, setting) in Walk(settings, prefix))
        {
            var rules = setting.Rules;
            var kind = setting.ItemKind ?? setting.Kind;
            var words = new List<string>();
            if (rules.Required)
            {
                words.Add("required");
            }

            if (rules.Minimum is not null && rules.Minimum != IntMinimum)
            {
                words.Add($"{rules.Minimum}..{rules.Maximum}");
            }

            int? length = kind switch
            {
                FieldSettingKind.Text or FieldSettingKind.LocalizedText => FieldSettingRules.TextLength,
                FieldSettingKind.LocalizedHtml => FieldSettingRules.HtmlLength,
                _ => null,
            };
            if (rules.MaximumLength != length || rules.MinimumLength is not null)
            {
                words.Add(rules.MinimumLength is { } fewest ? $"chars {fewest}..{rules.MaximumLength}" : $"chars<={rules.MaximumLength}");
            }

            if (setting.Kind == FieldSettingKind.List && (rules.MaximumCount != FieldSettingRules.ListCount || rules.MinimumCount is not null))
            {
                words.Add($"items {rules.MinimumCount ?? 0}..{rules.MaximumCount}");
            }

            if (rules.AllowedValues.Length > 0)
            {
                words.Add($"one of {string.Join('|', rules.AllowedValues)}");
            }

            if (words.Count > 0)
            {
                yield return $"{path} {string.Join(' ', words)}";
            }
        }
    }

    // Every setting with its place, those inside objects of settings too.
    private static IEnumerable<(string Path, FieldSettingDescriptor Setting)> Walk(IEnumerable<FieldSettingDescriptor> settings, string prefix)
    {
        foreach (var setting in settings)
        {
            var path = prefix + setting.Name;
            yield return (path, setting);
            foreach (var inner in Walk(setting.Settings, path + "."))
            {
                yield return inner;
            }
        }
    }

    private static JsonNode Text(FieldSettingKind kind, string text) =>
        kind == FieldSettingKind.Text ? JsonValue.Create(text) : new JsonObject { [string.Empty] = text };

    // A value at a setting's place, alone in the settings: with no problem there, or with one of the code given. A
    // list holds it as its only item, a localised text as its default text.
    private static void Holds(FieldSettingsSchema schema, string path, FieldSettingDescriptor setting, JsonNode value, string? code)
    {
        var steps = path.Split('.');
        var settings = new JsonObject();
        var parent = settings;
        foreach (var step in steps[..^1])
        {
            var inner = new JsonObject();
            parent[step] = inner;
            parent = inner;
        }

        parent[steps[^1]] = setting.Kind == FieldSettingKind.List ? new JsonArray(value) : value;
        var at = $"$.{path}" + setting.Kind switch
        {
            FieldSettingKind.List => "[0]",
            FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml => "['']",
            _ => string.Empty,
        };
        var found = Check(schema, SettingsObject.Parse(settings.ToJsonString())).Where(problem => problem.EndsWith($" at {at}", StringComparison.Ordinal));

        Assert.That(found, Is.EqualTo(code is null ? Array.Empty<string>() : [$"{code} at {at}"]), $"{path} = {value.ToJsonString()}");
    }

    private static List<string> Check(FieldSettingsSchema schema, SettingsObject? settings)
    {
        var problems = new DefinitionProblems();
        FieldSettingsChecks.Check(schema, settings, "$", "A field", problems);
        return [.. problems.ToReadOnlyList().Select(problem => $"{problem.Code} at {problem.Path}")];
    }

    // A field of the type with the parts it cannot do without, and these settings over its required ones.
    private static JsonObject Field(string type, JsonObject settings)
    {
        static JsonObject Text(string text) => new() { [string.Empty] = text };
        var traits = TestFieldTypes.Registry.Find(type)!;
        var field = new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000001", ["type"] = type };
        if (traits.HoldsValue)
        {
            field["alias"] = "subject";
        }

        if (traits.Supports(FieldFeatures.Options))
        {
            field["options"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["value"] = "a" }) };
        }

        if (traits.Supports(FieldFeatures.Rows))
        {
            field["rows"] = new JsonArray(new JsonObject { ["key"] = "31000000-0000-4000-8000-000000000001" });
        }

        if (traits.Supports(FieldFeatures.ChildFields))
        {
            field["fields"] = new JsonArray(new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000003", ["type"] = "text", ["alias"] = "child" });
        }

        var required = type switch
        {
            "calculation" => new JsonObject { ["formula"] = "amount * 2" },
            "consent" => new JsonObject { ["text"] = Text("I agree to the terms.") },
            "heading" => new JsonObject { ["text"] = Text("Your visit") },
            "paragraph" => new JsonObject { ["html"] = Text("<p>Welcome.</p>") },
            "image" => new JsonObject { ["media"] = "5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c", ["alt"] = Text("A view of the lake") },
            _ => [],
        };
        foreach (var (name, value) in settings)
        {
            required[name] = value?.DeepClone();
        }

        if (required.Count > 0)
        {
            field["settings"] = required;
        }

        return field;
    }

    private static List<string> Publish(JsonObject field) =>
        [.. StructureTests.Publish(Document(field)).Select(problem => $"{problem.Code} at {problem.Path}")];

    // The field, and a number a calculation's formula reads.
    private static string Document(JsonObject field) => new JsonObject
    {
        ["schemaVersion"] = 1,
        ["key"] = "f0000000-0000-4000-8000-000000000001",
        ["pages"] = new JsonArray(new JsonObject
        {
            ["key"] = "10000000-0000-4000-8000-000000000001",
            ["groups"] = new JsonArray(new JsonObject
            {
                ["key"] = "20000000-0000-4000-8000-000000000001",
                ["fields"] = new JsonArray(field, new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000004", ["type"] = "number", ["alias"] = "amount" }),
            }),
        }),
    }.ToJsonString();
}
