using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The host's designer locks of field settings (ADR-0060): which locks fit the site's field types and how start-up names
/// the others without repeating their values, how a lock's text is read by its setting's kind, the definition as the
/// site runs it, and the draft saves that give a locked setting a value of its own.
/// </summary>
public sealed class FieldSettingLocksTests
{
    private const string Key = FormBuilderOptions.SectionName + ":Designer:SettingLocks:";

    private static readonly Guid First = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("30000000-0000-4000-8000-000000000002");
    private static readonly Guid Third = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static readonly Guid Fourth = Guid.Parse("30000000-0000-4000-8000-000000000004");

    private static FieldTypeRegistry Registry => TestFieldTypes.Registry;

    // A lock's text, and the value every field gets as settings store it ("-" when the lock keeps the default).
    public static IEnumerable<TestCaseData> AcceptedValues()
    {
        static TestCaseData Value(string type, string setting, string? text, string expected) =>
            new TestCaseData(type, setting, text, expected).SetName($"Accepted({type}.{setting} = {text ?? "none"})");

        yield return Value("text", "maxLength", "100", "100");
        yield return Value("text", "minLength", "007", "7");
        yield return Value("text", "minLength", "-0", "0");
        yield return Value("text", "counter", "True", "true");
        yield return Value("text", "counter", "FALSE", "false");
        yield return Value("number", "min", "2.50", "\"2.50\"");
        yield return Value("text", "prefix", "EUR", """{"":"EUR"}""");
        yield return Value("text", "prefix", "{{ member.name }}", """{"":"{{ member.name }}"}""");
        yield return Value("radio", "layout", "cards", "\"cards\"");
        yield return Value("time", "min", "09:30", "\"09:30\"");
        yield return Value("calculation", "formula", "price * 2", "\"price * 2\"");
        yield return Value("url", "schemes", """["https", "http"]""", """["https","http"]""");
        yield return Value("email", "domains", """{"mode": "allow", "list": ["example.com"]}""", """{"list":["example.com"],"mode":"allow"}""");
        yield return Value("date", "min", "\"2026-01-01\"", "\"2026-01-01\"");
        yield return Value("file", "extensions", "[]", "-");
        yield return Value("email", "domains", """{"list": []}""", "-");
        yield return Value("date", "min", "null", "-");
        yield return Value("text", "maxLength", null, "-");
    }

    // A lock's text that its setting refuses, and what the setting takes in words.
    public static IEnumerable<TestCaseData> RefusedValues()
    {
        static TestCaseData Value(string type, string setting, string text, string takes, string name) =>
            new TestCaseData(type, setting, text, takes).SetName($"Refused({type}.{setting}: {name})");

        const string Schemes = "a list written as JSON, with at most 1000 items, each item one of 'https', 'http'";
        const string Domains = "an object written as JSON, with the settings 'mode' and 'list'";
        const string Json = "JSON, which the field type checks when a form is published";
        yield return Value("text", "maxLength", "5000", "a whole number, 1 to 4000", "above its range");
        yield return Value("text", "maxLength", "12.5", "a whole number, 1 to 4000", "a decimal");
        yield return Value("text", "maxLength", string.Empty, "a whole number, 1 to 4000", "empty");
        yield return Value("text", "maxLength", " 100", "a whole number, 1 to 4000", "with spaces");
        yield return Value("file", "maxBytes", "99999999999999999999", "a whole number, 1 to 999999999999999", "beyond a whole number");
        yield return Value("number", "decimals", "16", "a whole number, 0 to 15", "above its range");
        yield return Value("text", "counter", "yes", "true or false", "not a boolean");
        yield return Value("radio", "layout", "grid", "one of 'inherit', 'vertical', 'horizontal', 'cards'", "not a choice");
        yield return Value("text", "prefix", new string('x', 51), "a text of at most 50 characters", "too long");
        yield return Value("text", "prefix", ((char)0xD800).ToString(), "a text of at most 50 characters", "a lone surrogate");
        yield return Value("consent", "text", " ", "a text that is not blank, of at most 20000 characters", "blank");
        yield return Value("calculation", "formula", "  ", "a text that is not blank, of at most 1000 characters", "blank");
        yield return Value("time", "min", "25:00", "a time of day, HH:mm", "not a time");
        yield return Value("number", "min", "2,5", "a number such as 2.5", "a comma");
        yield return Value("url", "schemes", "gopher", Schemes, "not JSON");
        yield return Value("url", "schemes", """["ftp"]""", Schemes, "not a choice");
        yield return Value("url", "schemes", """["https", "https"]""", Schemes, "twice");
        yield return Value("url", "schemes", """["https", null]""", Schemes, "a null");
        yield return Value("date", "excluded", """["2026-02-30"]""", "a list written as JSON, with at most 1000 items, each item a date, YYYY-MM-DD", "not a date");
        yield return Value("file", "extensions", """["averyverylongone"]""", "a list written as JSON, with at most 1000 items, each item a text of at most 10 characters", "too long");
        yield return Value("email", "domains", """{"mode": "some"}""", Domains, "not a choice");
        yield return Value("email", "domains", """{"colour": true}""", Domains, "no such setting");
        yield return Value("email", "domains", """{"mode": "any", "mode": "allow"}""", Domains, "a setting twice");
        yield return Value("date", "min", """{"days": 1.5}""", Json, "a decimal");
        yield return Value("date", "min", "{", Json, "not JSON");
    }

    [TestCaseSource(nameof(AcceptedValues))]
    public void ALocksText_IsReadByItsSettingsKind_AsSettingsStoreIt(string type, string setting, string? text, string expected)
    {
        var locks = Locks(Lock(type, setting, text));

        var found = locks.Of(type).Single();

        Assert.Multiple(() =>
        {
            Assert.That(found.Value?.GetRawText() ?? "-", Is.EqualTo(expected));
            Assert.That((found.Type, found.Setting.Name, found.ConfigurationKey, found.Hidden), Is.EqualTo((type, setting, Key + "0", false)));
        });
    }

    [TestCaseSource(nameof(RefusedValues))]
    public void AValueTheSettingRefuses_StopsTheSite_SayingWhatItTakes_WithoutTheValue(string type, string setting, string text, string takes)
    {
        var problems = FieldSettingLocks.Problems([Lock(type, setting, text)], Registry);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.EqualTo(new[] { $"{Key}0:Value is not a value of the setting '{setting}' of the field type '{type}', which takes {takes}." }));
            if (text.Trim().Length > 3)
            {
                Assert.That(problems[0], Does.Not.Contain(text), "the value is never repeated");
            }
        });
    }

    [Test]
    public void ASiteTypesValueTheSettingRefuses_IsNamedWithWhatItTakes()
    {
        var registry = TestFieldTypes.Build(builder => builder.Append<LockProbeFieldType>());

        var problems = FieldSettingLocks.Problems(
            [
                Lock("acme.probe", "side", "up"),
                Lock("acme.probe", "mode", "many"),
                Lock("acme.probe", "box", """{"colour":"red"}"""),
                Lock("acme.probe", "code", "x"),
                Lock("acme.probe", "page", "home"),
                Lock("acme.probe", "tags", """["a"]"""),
            ],
            registry);

        Assert.That(problems, Is.EqualTo(new[]
        {
            $"{Key}0:Value is not a value of the setting 'side' of the field type 'acme.probe', which takes one of 'left', 'right'.",
            $"{Key}1:Value is not a value of the setting 'mode' of the field type 'acme.probe', which takes 'only'.",
            $"{Key}2:Value is not a value of the setting 'box' of the field type 'acme.probe', which takes an object written as JSON, with the settings 'label'.",
            $"{Key}3:Value is not a value of the setting 'code' of the field type 'acme.probe', which takes a text of 2 to 10 characters.",
            $"{Key}4:Value is not a value of the setting 'page' of the field type 'acme.probe', which takes a key, such as 5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c.",
            $"{Key}5:Value is not a value of the setting 'tags' of the field type 'acme.probe', which takes a list written as JSON, with 2 to 3 items, each item a text of at most 4 characters.",
        }));
    }

    [Test]
    public void ALockOnATypeOrASettingTheSiteLacks_StopsTheSite_NamingItsKey()
    {
        var aliases = string.Join(", ", Registry.Types.Take(Registry.Types.Length - 1).Select(type => $"'{type.Declaration.Alias}'"));

        var problems = FieldSettingLocks.Problems(
            [
                Lock("text", "maxLength", "100"),
                Lock("acme.colour", "swatches"),
                Lock("Text", "maxLength"),
                Lock("text", "colour"),
                Lock("text", "maxlength"),
                Lock("divider", "colour"),
                Lock("consent", "text"),
            ],
            Registry);

        Assert.That(problems, Is.EqualTo(new[]
        {
            $"{Key}1:Type ('acme.colour') is not a field type of the site; its field types are {aliases} and 'captcha'.",
            $"{Key}2:Type ('Text') is not a field type of the site; aliases are compared exactly, and the site has 'text'.",
            $"{Key}3:Setting ('colour') is not a setting of the field type 'text'; its settings are 'minLength', 'maxLength', 'prefix', 'suffix' and 'counter'.",
            $"{Key}4:Setting ('maxlength') is not a setting of the field type 'text'; names are compared exactly, and it has 'maxLength'.",
            $"{Key}5:Setting ('colour') is not a setting of the field type 'divider', which has no settings.",
            $"{Key}6 locks the required setting 'text' of the field type 'consent' without a value; a field cannot do without one, so give the lock its Value.",
        }));
    }

    [Test]
    public void LocksOfAnotherShape_OrOfAnActionType_AreLeftToTheirOwnChecks()
    {
        var problems = FieldSettingLocks.Problems(
            [
                null!,
                Lock("email", "from", "noreply@example.com", kind: DesignerSettingLock.ActionKind),
                Lock("text", "maxLength", "100", kind: "fields"),
                Lock("1text", "maxLength"),
                Lock("text", "MaxLength"),
                Lock("text", "prefix", new string('x', FormBuilderOptionsValidator.MaxLockValueLength + 1)),
                Lock("text", "maxLength", "100", kind: "FIELD"),
                Lock("text", "maxLength", "5000"),
            ],
            Registry);

        var locks = Locks(Lock("text", "maxLength", "100", kind: "Field"), Lock("text", "maxLength", "5000"), Lock("email", "from", kind: DesignerSettingLock.ActionKind));

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, "a second lock of one setting is the options' to refuse");
            Assert.That(locks.Of("text").Select(each => each.Value!.Value.GetRawText()), Is.EqualTo(new[] { "100" }), "the first lock counts");
            Assert.That(locks.Of("email"), Is.Empty, "the action types come in 8.1");
        });
    }

    [Test]
    public void TheLocks_RefuseToBeCreated_WhenOneDoesNotFit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Locks(Lock("text", "maxLength", "5000"), Lock("acme.colour", "swatches")), Throws.InvalidOperationException.With.Message.EqualTo(
                $"{Key}0:Value is not a value of the setting 'maxLength' of the field type 'text', which takes a whole number, 1 to 4000.{Environment.NewLine}"
                + $"{Key}1:Type ('acme.colour') is not a field type of the site; its field types are {string.Join(", ", Registry.Types.Take(Registry.Types.Length - 1).Select(type => $"'{type.Declaration.Alias}'"))} and 'captcha'."));
            Assert.That(Locks().IsEmpty, Is.True);
            Assert.That(Locks(Lock("text", "counter", hidden: true)).Of("text").Single(), Is.EqualTo(new FieldSettingLock("text", Registry.FindType("text")!.SettingsSchema.Find("counter")!, Key + "0", null, true)));
            Assert.That(Locks(Lock("text", "counter")).Of("textarea"), Is.Empty);
            Assert.That(() => new FieldSettingLocks(null!, Registry), Throws.ArgumentNullException);
            Assert.That(() => FieldSettingLocks.Problems(null!, Registry), Throws.ArgumentNullException);
            Assert.That(() => FieldSettingLocks.Problems([], null!), Throws.ArgumentNullException);
            Assert.That(() => Locks().Of(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void TheSite_StartsWithLocksThatFit_AndStopsOnOnesThatDoNot_LeavingInvalidOptionsToTheirOwnCheck()
    {
        var lacksColour = new Dictionary<string, string?> { [Key + "0:Kind"] = "field", [Key + "0:Type"] = "text", [Key + "0:Setting"] = "colour" };
        using var valid = Site(new Dictionary<string, string?> { [Key + "0:Kind"] = "field", [Key + "0:Type"] = "text", [Key + "0:Setting"] = "maxLength", [Key + "0:Value"] = "100" });
        using var lacking = Site(lacksColour);
        using var taken = Site(lacksColour, builder => builder.Append<FieldTypeRegistryTests.OwnTextFieldType>());
        using var refused = Site(new Dictionary<string, string?> { [Key + "0:Kind"] = "field", [Key + "0:Type"] = "text", [Key + "0:Setting"] = "counter", [Key + "0:Value"] = "sometimes" });
        using var malformed = Site(new Dictionary<string, string?>
        {
            [Key + "0:Kind"] = "fields",
            [Key + "0:Type"] = "text",
            [Key + "0:Setting"] = "counter",
            [Key + "1:Kind"] = "field",
            [Key + "1:Type"] = "acme.colour",
            [Key + "1:Setting"] = "swatches",
        });

        Assert.Multiple(() =>
        {
            Assert.That(() => HostOptions.Start(valid), Throws.Nothing);
            Assert.That(valid.GetRequiredService<FieldSettingLocks>().Of("text").Single().Value!.Value.GetInt32(), Is.EqualTo(100));
            Assert.That(() => HostOptions.Start(lacking), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                $"{Key}0:Setting ('colour') is not a setting of the field type 'text'; its settings are 'minLength', 'maxLength', 'prefix', 'suffix' and 'counter'."));
            Assert.That(() => HostOptions.Start(refused), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                $"{Key}0:Value is not a value of the setting 'counter' of the field type 'text', which takes true or false.").And.Message.Not.Contains("sometimes"));
            Assert.That(() => HostOptions.Start(malformed), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                $"{Key}0:Kind ('fields') must be 'field' or 'action'.").And.Message.Not.Contains("acme.colour"));
            Assert.That(() => HostOptions.Start(taken), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                $"The field type 'text' ({typeof(FieldTypeRegistryTests.OwnTextFieldType).FullName}) has the alias").And.Message.Not.Contains(Key + "0"),
                "field types that cannot be a registry are their own check's");
        });
    }

    [Test]
    public void TheDefinitionTheSiteRuns_HasTheLockedValues_InEveryFieldOfTheType_ChildFieldsToo()
    {
        var locks = Locks(Lock("text", "maxLength", "100"), Lock("text", "prefix", hidden: true));
        var definition = Form(
            Field(First, "text", """{"maxLength":500,"prefix":{"":"A"},"counter":true}"""),
            Field(Second, "text"),
            Field(Third, "textarea", """{"maxLength":500}"""),
            Field(Fourth, "repeater", null, Field(Guid.NewGuid(), "text", """{"prefix":{"":"B"}}""")));

        var running = locks.Apply(definition);
        var fields = running.Pages[0].Groups[0].Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(field => field.Settings?.ToString()), Is.EqualTo(new[] { """{"counter":true,"maxLength":100}""", """{"maxLength":100}""", """{"maxLength":500}""", null }));
            Assert.That(fields[3].Fields[0].Settings?.ToString(), Is.EqualTo("""{"maxLength":100}"""), "a child field is a field too");
            Assert.That(fields[2], Is.SameAs(definition.Pages[0].Groups[0].Fields[2]), "a field of another type stays as it is");
            Assert.That(definition.Pages[0].Groups[0].Fields[0].Settings!.ToString(), Is.EqualTo("""{"counter":true,"maxLength":500,"prefix":{"":"A"}}"""), "the stored definition keeps its values");
            Assert.That((running.Key, running.Settings), Is.EqualTo((definition.Key, definition.Settings)));
        });
    }

    [Test]
    public void ADefinitionNoLockChanges_IsTheSameInstance_AndALockThatLeavesNothing_LeavesNoSettings()
    {
        var unchanged = Form(Field(First, "text", """{"maxLength":100}"""), Field(Second, "textarea", """{"maxLength":500}"""), Field(Third, "repeater", null, Field(Fourth, "textarea")));
        var prefixOnly = Form(Field(First, "text", """{"prefix":{"":"A"}}"""));

        Assert.Multiple(() =>
        {
            Assert.That(Locks(Lock("text", "maxLength", "100")).Apply(unchanged), Is.SameAs(unchanged));
            Assert.That(Locks().Apply(unchanged), Is.SameAs(unchanged));
            Assert.That(Locks(Lock("text", "prefix")).Apply(prefixOnly).Pages[0].Groups[0].Fields[0].Settings, Is.Null, "settings with nothing in them are written as none");
            Assert.That(Locks(Lock("text", "counter")).Apply(Form(Field(First, "text"))).Pages[0].Groups[0].Fields[0].Settings, Is.Null);
            Assert.That(() => Locks().Apply(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ADraftSave_RefusesALockedSettingWithAValueOfItsOwn_NamingItsPathAndTheLock()
    {
        var locks = SaveLocks();

        Assert.Multiple(() =>
        {
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"maxLength":500}"""))), Is.EqualTo((At(0, "maxLength"), Key + "0")));
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"maxLength":100,"minLength":5}"""))), Is.Null, "the lock's value, and a setting no lock names");
            Assert.That(Refusal(locks, Form(Field(First, "text"))), Is.Null, "a setting the field leaves out");
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"prefix":{"":"A"}}"""))), Is.EqualTo((At(0, "prefix"), Key + "1")), "a hidden setting without a default takes no value");
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"counter":false}"""))), Is.Null, "the default, for a lock without a value");
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"counter":true}"""))), Is.EqualTo((At(0, "counter"), Key + "2")));
            Assert.That(Refusal(locks, Form(Field(First, "textarea", """{"maxLength":500}"""))), Is.Null, "another type");
            Assert.That(Refusal(Locks(), Form(Field(First, "text", """{"maxLength":500}"""))), Is.Null, "no locks");
        });
    }

    [Test]
    public void ADraftSave_ComparesValuesAsTheirKindDoes()
    {
        var locks = SaveLocks();

        Assert.Multiple(() =>
        {
            Assert.That(Refusal(locks, Form(Field(First, "number", """{"min":"2.50"}"""))), Is.Null, "2.50 is 2.5");
            Assert.That(Refusal(locks, Form(Field(First, "number", """{"min":"3"}"""))), Is.EqualTo((At(0, "min"), Key + "3")));
            Assert.That(Refusal(locks, Form(Field(First, "url", """{"schemes":["https","http"]}"""))), Is.Null);
            Assert.That(Refusal(locks, Form(Field(First, "url", """{"schemes":["http","https"]}"""))), Is.EqualTo((At(0, "schemes"), Key + "4")), "a list keeps its order");
            Assert.That(Refusal(locks, Form(Field(First, "url", """{"schemes":["https"]}"""))), Is.EqualTo((At(0, "schemes"), Key + "4")));
            Assert.That(Refusal(locks, Form(Field(First, "time", """{"min":"09:30:00"}"""))), Is.Null, "09:30:00 is 09:30");
            Assert.That(Refusal(locks, Form(Field(First, "time", """{"min":"09:31"}"""))), Is.EqualTo((At(0, "min"), Key + "5")));
            Assert.That(Refusal(locks, Form(Field(First, "date", """{"excluded":["2026-01-01","2026-12-25"]}"""))), Is.Null);
            Assert.That(Refusal(locks, Form(Field(First, "date", """{"excluded":["2026-12-25"]}"""))), Is.EqualTo((At(0, "excluded"), Key + "6")));
            Assert.That(Refusal(locks, Form(Field(First, "email", """{"domains":{"mode":"allow","list":["example.com"]}}"""))), Is.Null, "an object as JSON, in any order");
            Assert.That(Refusal(locks, Form(Field(First, "email", """{"domains":{"mode":"block","list":["example.com"]}}"""))), Is.EqualTo((At(0, "domains"), Key + "7")));
        });
    }

    [Test]
    public void ADraftSave_ComparesAListItemByItem_AsItsItemsKindDoes()
    {
        var options = new FormBuilderOptions();
        options.Designer.SettingLocks.Add(Lock("probe", "ratios", """["2.5","10"]"""));
        options.Designer.SettingLocks.Add(Lock("probe", "times", """["09:30"]"""));
        var locks = new FieldSettingLocks(Options.Create(options), TestFieldTypes.Build(builder => builder.Append<FieldSettingsChecksTests.ProbeFieldType>()));

        Assert.Multiple(() =>
        {
            Assert.That(Refusal(locks, Form(Field(First, "probe", """{"ratios":["2.50","10.0"],"times":["09:30:00"]}"""))), Is.Null, "numbers and times as their values");
            Assert.That(Refusal(locks, Form(Field(First, "probe", """{"ratios":["2.5"]}"""))), Is.EqualTo((At(0, "ratios"), Key + "0")), "fewer items");
            Assert.That(Refusal(locks, Form(Field(First, "probe", """{"ratios":["10","2.5"]}"""))), Is.EqualTo((At(0, "ratios"), Key + "0")), "another order");
            Assert.That(Refusal(locks, Form(Field(First, "probe", """{"times":["09:31"]}"""))), Is.EqualTo((At(0, "times"), Key + "1")));
        });
    }

    [Test]
    public void ADraftSave_KeepsAValueTheStoredDraftHeld_ForTheSameFieldAndType()
    {
        var locks = SaveLocks();
        var stored = Form(Field(First, "text", """{"maxLength":500}"""), Field(Second, "textarea", """{"maxLength":500}"""), Field(Third, "text", """{"counter":true}"""));

        Assert.Multiple(() =>
        {
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"maxLength":500}""")), stored), Is.Null, "a value from before the lock came");
            Assert.That(Refusal(locks, Form(Field(Third, "text", """{"counter":true}"""), Field(First, "text", """{"maxLength":500,"minLength":2}""")), stored), Is.Null, "moved, beside other changes");
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"maxLength":400}""")), stored), Is.EqualTo((At(0, "maxLength"), Key + "0")), "changed");
            Assert.That(Refusal(locks, Form(Field(Second, "text", """{"maxLength":500}""")), stored), Is.EqualTo((At(0, "maxLength"), Key + "0")), "another type before");
            Assert.That(Refusal(locks, Form(Field(Fourth, "text", """{"maxLength":500}""")), stored), Is.EqualTo((At(0, "maxLength"), Key + "0")), "another field");
            Assert.That(Refusal(locks, Form(Field(First, "text", """{"counter":true}""")), stored), Is.EqualTo((At(0, "counter"), Key + "2")), "a value the field did not hold");
            Assert.That(Refusal(locks, Form(Field(First, "number", """{"min":"3"}""")), Form(Field(First, "number", """{"min":"3.0"}"""))), Is.Null, "compared as its kind compares");
        });
    }

    [Test]
    public void ADraftSave_NamesTheFirstLockedSettingInDocumentOrder_ChildFieldsIncluded()
    {
        var locks = SaveLocks();

        Assert.Multiple(() =>
        {
            Assert.That(
                Refusal(locks, Form(Field(First, "text", """{"counter":true,"maxLength":500}"""), Field(Second, "text", """{"maxLength":600}"""))),
                Is.EqualTo((At(0, "maxLength"), Key + "0")),
                "the locks of a field in the configuration's order");
            Assert.That(
                Refusal(locks, Form(Field(First, "repeater", null, Field(Second, "text", """{"maxLength":500}""")), Field(Third, "text", """{"counter":true}"""))),
                Is.EqualTo(("$.pages[0].groups[0].fields[0].fields[0].settings.maxLength", Key + "0")));
            Assert.That(
                Refusal(locks, Form(Field(First, "repeater", """{"maxRows":20}""", Field(Second, "text", """{"maxLength":500}""")))),
                Is.EqualTo((At(0, "maxRows"), Key + "8")),
                "a field before its child fields");
            Assert.That(() => locks.CheckSave(null!, null), Throws.ArgumentNullException);
        });
    }

    private static FieldSettingLocks SaveLocks() => Locks(
        Lock("text", "maxLength", "100"),
        Lock("text", "prefix", hidden: true),
        Lock("text", "counter"),
        Lock("number", "min", "2.5"),
        Lock("url", "schemes", """["https","http"]"""),
        Lock("time", "min", "09:30"),
        Lock("date", "excluded", """["2026-01-01","2026-12-25"]"""),
        Lock("email", "domains", """{"list":["example.com"],"mode":"allow"}"""),
        Lock("repeater", "maxRows", "5"));

    private static (string Field, string Key)? Refusal(FieldSettingLocks locks, FormDefinition definition, FormDefinition? stored = null)
    {
        try
        {
            locks.CheckSave(definition, stored);
            return null;
        }
        catch (FormBuilderSettingLockedException exception)
        {
            return (exception.Field, exception.ConfigurationKey);
        }
    }

    private static string At(int field, string setting) => $"$.pages[0].groups[0].fields[{field}].settings.{setting}";

    private static FieldSettingLocks Locks(params DesignerSettingLock[] locks)
    {
        var options = new FormBuilderOptions();
        foreach (var each in locks)
        {
            options.Designer.SettingLocks.Add(each);
        }

        return new FieldSettingLocks(Options.Create(options), Registry);
    }

    private static DesignerSettingLock Lock(string type, string setting, string? value = null, bool hidden = false, string kind = DesignerSettingLock.FieldKind) =>
        new() { Kind = kind, Type = type, Setting = setting, Value = value, Hidden = hidden };

    private static FormDefinition Form(params FormField[] fields) => new()
    {
        Key = Guid.Parse("f0000000-0000-4000-8000-000000000001"),
        Pages =
        [
            new FormPage
            {
                Key = Guid.Parse("10000000-0000-4000-8000-000000000001"),
                Groups = [new FormGroup { Key = Guid.Parse("20000000-0000-4000-8000-000000000001"), Fields = fields }],
            },
        ],
    };

    private static FormField Field(Guid key, string type, string? settings = null, params FormField[] children) =>
        new() { Key = key, Type = type, Settings = settings is null ? null : SettingsObject.Parse(settings), Fields = children };

    /// <summary>A site's own type whose settings take a word from a list, a single word, an object of one setting, a text with a
    /// fewest length, a key and a list with a fewest count.</summary>
    public sealed class LockProbeFieldType() : FormBuilderFieldTypeBase<LockProbeSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.probe",
        Name = "Probe",
        Icon = "icon-settings",
        PaletteGroup = FieldPaletteGroup.Content,
        Features = FieldFeatures.VisibilityRules,
    });

    /// <summary>The probe's settings.</summary>
    public sealed class LockProbeSettings
    {
        /// <summary>A word from a list.</summary>
        [AllowedValues("left", "right")]
        [FormBuilderSetting("Side")]
        public string? Side { get; init; }

        /// <summary>The one word it takes.</summary>
        [AllowedValues("only")]
        [FormBuilderSetting("Mode")]
        public string? Mode { get; init; }

        /// <summary>An object of one setting.</summary>
        [FormBuilderSetting("Box")]
        public LockProbeBox? Box { get; init; }

        /// <summary>A text of 2 to 10 characters.</summary>
        [StringLength(10, MinimumLength = 2)]
        [FormBuilderSetting("Code")]
        public string? Code { get; init; }

        /// <summary>A key.</summary>
        [FormBuilderSetting("Page", Editor = "contentPicker")]
        public Guid? Page { get; init; }

        /// <summary>A list of 2 to 3 short texts.</summary>
        [Length(2, 3)]
        [StringLength(4)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = ["a", "b"];
    }

    /// <summary>The probe's object.</summary>
    public sealed class LockProbeBox
    {
        /// <summary>Its one setting.</summary>
        [FormBuilderSetting("Label")]
        public string? Label { get; init; }
    }

    // The services of a site with the host configuration and its composers' field types, as the core composer registers
    // them, with the logging and the clock every Umbraco site has.
    private static ServiceProvider Site(IReadOnlyDictionary<string, string?> settings, Action<FormBuilderFieldTypeCollectionBuilder>? change = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(HostOptions.Folders());
        services.AddFormBuilderOptions(HostOptions.Configuration(settings));
        var builder = new FormBuilderFieldTypeCollectionBuilder();
        change?.Invoke(builder);
        builder.RegisterWith(services);
        TestFieldTypes.AddCaptchaProviders(services);
        services.AddFieldTypeRegistry();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
