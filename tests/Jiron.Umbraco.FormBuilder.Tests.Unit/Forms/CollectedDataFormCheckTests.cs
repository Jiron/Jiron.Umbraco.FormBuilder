using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The form check's checks of what a form collects (ROADMAP 9.5e, ADR-0180): personal data without a consent field, a
/// card number or a password asked for, a file field without a size, a required choice whose options can all fill up,
/// limits that count entries the form does not keep, and a last outcome with a rule.
/// </summary>
[TestFixture]
public sealed class CollectedDataFormCheckTests
{
    private const string Fields = "$.pages[0].groups[0].fields";

    private static readonly FieldTypeRegistry Types = TestFieldTypes.Build(builder => builder.Append<PassportType>());

    [Test]
    public async Task PersonalData_WithoutAConsentField_IsAHint_AtTheFirstFieldThatAsksForIt()
    {
        var issues = await new ConsentFormCheck(Types).CheckAsync(Form(Text("F1", "Topic"), Email("F2")), CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Hint, ConsentFormCheck.ConsentMissing, Fields + "[1]") }));
    }

    [Test]
    public async Task PersonalData_IsATypesByItsNature_OrAnAutocompleteTokenThatNamesReachesOrLocatesAPerson()
    {
        async Task<string?> FirstAsync(params string[] fields) =>
            (await new ConsentFormCheck(Types).CheckAsync(Form(fields), CancellationToken.None)).SingleOrDefault()?.Path;

        Assert.Multiple(async () =>
        {
            Assert.That(await FirstAsync(Text("F1", "Street", autocomplete: "section-ship shipping street-address")), Is.EqualTo(Fields + "[0]"), "a token after its section and address kind");
            Assert.That(await FirstAsync(Text("F1", "Mail", autocomplete: "work email webauthn")), Is.EqualTo(Fields + "[0]"), "a token before webauthn");
            Assert.That(await FirstAsync("""{"key":"F1","type":"phone","alias":"phone","label":{"":"Phone"}}"""), Is.EqualTo(Fields + "[0]"));
            Assert.That(await FirstAsync("""{"key":"F1","type":"name","alias":"who","label":{"":"Name"}}"""), Is.EqualTo(Fields + "[0]"));
            Assert.That(await FirstAsync("""{"key":"F1","type":"address","alias":"where","label":{"":"Address"}}"""), Is.EqualTo(Fields + "[0]"));
            Assert.That(await FirstAsync("""{"key":"F1","type":"signature","alias":"sign","label":{"":"Sign"}}"""), Is.EqualTo(Fields + "[0]"));
            Assert.That(await FirstAsync("""{"key":"F1","type":"acme.passport","alias":"passport","label":{"":"Passport"}}"""), Is.EqualTo(Fields + "[0]"), "a site's own type that declares it");
            Assert.That(await FirstAsync("""{"key":"F1","type":"repeater","alias":"guests","label":{"":"Guests"},"fields":[{"key":"F2","type":"email","alias":"mail","label":{"":"Mail"}}]}"""), Is.EqualTo(Fields + "[0].fields[0]"), "a repeatable group's child");
            Assert.That(await FirstAsync(Text("F1", "Topic"), Text("F2", "Country", autocomplete: "country-name"), """{"key":"F3","type":"number","alias":"count","label":{"":"Count"}}"""), Is.Null, "no person in a topic, a country or a count");
        });
    }

    [Test]
    public async Task AConsentField_AnywhereInTheForm_LeavesNoHint()
    {
        var issues = await new ConsentFormCheck(Types).CheckAsync(
            Form(Email("F1"), """{"key":"F2","type":"repeater","alias":"more","label":{"":"More"},"fields":[{"key":"F3","type":"consent","alias":"ok","label":{"":"OK"},"settings":{"text":{"":"I agree"}}}]}"""),
            CancellationToken.None);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public async Task ACardNumber_IsAWarning_FromItsToken_ItsLabelOrPlaceholderInAnyCulture_OrItsPattern()
    {
        var accented = $"Num{(char)0xE9}ro de carte";
        var definition = Form(
            Text("F1", "Number", autocomplete: "billing cc-number"),
            """{"key":"F2","type":"text","alias":"card","label":{"":"Card details","de":"Kreditkartennummer"}}""",
            $$$"""{"key":"F3","type":"text","alias":"carte","label":{"":"Your card"},"placeholder":{"":"{{{accented}}}"}}""",
            """{"key":"F4","type":"text","alias":"digits","label":{"":"Digits"},"validation":[{"kind":"pattern","pattern":"[0-9 ]{13,23}"}]}""",
            """{"key":"30000000-0000-4000-8000-000000000005","type":"text","alias":"code","label":{"":"CVV"}}""",
            """{"key":"30000000-0000-4000-8000-000000000006","type":"text","alias":"number","label":{"":"Card number *"}}""");

        var issues = await new CardAndPasswordFormCheck(Types).CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, CardAndPasswordFormCheck.CardNumberAsked, Fields + "[0].autocomplete"),
            (FormCheckSeverity.Warning, CardAndPasswordFormCheck.CardNumberAsked, Fields + "[1].label.de"),
            (FormCheckSeverity.Warning, CardAndPasswordFormCheck.CardNumberAsked, Fields + "[2].placeholder['']"),
            (FormCheckSeverity.Warning, CardAndPasswordFormCheck.CardNumberAsked, Fields + "[3].validation[0].pattern"),
            (FormCheckSeverity.Warning, CardAndPasswordFormCheck.CardNumberAsked, Fields + "[4].label['']"),
            (FormCheckSeverity.Warning, CardAndPasswordFormCheck.CardNumberAsked, Fields + "[5].label['']"),
        }));
    }

    [Test]
    public async Task ACardsNumber_NamedWithoutTheCard_CountsOnlyAsTheWholeText_AndOtherCardsAndNumbersAreNone()
    {
        var definition = Form(
            Text("F1", "Membership card number"),
            Text("F2", "Library card no."),
            """{"key":"F3","type":"dropdown","alias":"kind","label":{"":"Card type"},"options":{"items":[{"value":"gold"}]}}""",
            """{"key":"F4","type":"text","alias":"member","label":{"":"Member number"},"validation":[{"kind":"pattern","pattern":"[0-9]+"}]}""",
            """{"key":"30000000-0000-4000-8000-000000000005","type":"text","alias":"phone","label":{"":"Phone"},"validation":[{"kind":"pattern","pattern":"[0-9]{6,12}"}]}""",
            """{"key":"30000000-0000-4000-8000-000000000006","type":"checkbox","alias":"terms","label":{"":"I pay by credit card"}}""");

        Assert.That(await new CardAndPasswordFormCheck(Types).CheckAsync(definition, CancellationToken.None), Is.Empty, "a checkbox is no field a visitor types into");
    }

    [Test]
    public async Task APassword_IsAWarning_FromItsToken_OrItsLabelOrPlaceholderInTheVisitorLanguages_NeverFromItsHelp()
    {
        var spanish = $"Contrase{(char)0xF1}a";
        var definition = Form(
            Text("F1", "Choose", autocomplete: "new-password"),
            Text("F2", "Mot de passe"),
            Text("F3", "Passwort wiederholen"),
            $$$"""{"key":"F4","type":"text","alias":"clave","label":{"":"Key"},"placeholder":{"es":"{{{spanish}}}","":"Key"}}""",
            Text("30000000-0000-4000-8000-000000000005", "Password hint"),
            Text("30000000-0000-4000-8000-000000000006", "Pass word"),
            """{"key":"30000000-0000-4000-8000-000000000007","type":"text","alias":"note","label":{"":"Note"},"help":{"":"We never ask for your password or credit card."}}""");

        var issues = await new CardAndPasswordFormCheck(Types).CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (CardAndPasswordFormCheck.PasswordAsked, Fields + "[0].autocomplete"),
            (CardAndPasswordFormCheck.PasswordAsked, Fields + "[1].label['']"),
            (CardAndPasswordFormCheck.PasswordAsked, Fields + "[2].label['']"),
            (CardAndPasswordFormCheck.PasswordAsked, Fields + "[3].placeholder.es"),
            (CardAndPasswordFormCheck.PasswordAsked, Fields + "[4].label['']"),
        }));
    }

    [TestCase("[0-9]{16}", true)]
    [TestCase("[0-9]{13,19}", true)]
    [TestCase("[0-9]{4}([ -]?[0-9]{4}){3}", true)]
    [TestCase("[0-9 -]+", false)]
    [TestCase("[0-9]{6,12}", false)]
    [TestCase("[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}", false)]
    [TestCase("(", false)]
    public void APattern_TakesCardNumbers_WhenItTakesSixteenDigitsAndRefusesTwelve(string pattern, bool takes) =>
        Assert.That(CardAndPasswordFormCheck.TakesCardNumbers(pattern), Is.EqualTo(takes));

    [Test]
    public void AText_IsFolded_ToLowercaseWordsWithoutAccents()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CardAndPasswordFormCheck.Folded($"N{(char)0xFA}mero  de-Tarjeta!"), Is.EqualTo(" numero de tarjeta "));
            Assert.That(CardAndPasswordFormCheck.Folded("CVV"), Is.EqualTo(" cvv "));
            Assert.That(CardAndPasswordFormCheck.Folded("--"), Is.EqualTo(" "));
        });
    }

    [Test]
    public async Task AFileField_WithoutASizeOfItsOwn_IsAHint_ThatNamesTheSitesLimit()
    {
        var definition = Form(
            """{"key":"F1","type":"file","alias":"cv","label":{"":"CV"}}""",
            """{"key":"F2","type":"file","alias":"photo","label":{"":"Photo"},"settings":{"maxBytes":1000000}}""",
            """{"key":"F3","type":"signature","alias":"sign","label":{"":"Sign"}}""");

        var issues = await new FileLimitFormCheck(Types, new FieldHostLimits(10_485_760, [])).CheckAsync(definition, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Hint, FileLimitFormCheck.FileSizeUnset, Fields + "[0].settings.maxBytes") }));
            Assert.That(issues[0].Message, Does.Contain("10,485,760 bytes each (Files:MaxFileBytes)"));
        });
    }

    [Test]
    public async Task ARequiredChoice_WhoseEveryOptionHasPlaces_IsAWarning_OwnOptionsOrALibraryListsThatHoldsThem()
    {
        var site = new OptionListTestSite();
        var full = Guid.Parse("70000000-0000-4000-8000-000000000001");
        var open = Guid.Parse("70000000-0000-4000-8000-000000000002");
        site.Repository.Write(full, OptionListKind.Manual, items: """[{"value":"a","places":3},{"value":"b","places":3}]""");
        site.Repository.Write(open, OptionListKind.Manual, items: """[{"value":"a","places":3},{"value":"b"}]""");
        var definition = Form(
            Choice("F1", """{"items":[{"value":"am","places":5},{"value":"pm","places":5}]}""", required: true),
            Choice("F2", """{"items":[{"value":"am","places":5},{"value":"pm"}]}""", required: true),
            Choice("F3", """{"items":[{"value":"am","places":5}]}"""),
            Choice("F4", """{"items":[{"value":"am","places":5}]}""", rule: """{"key":"40000000-0000-4000-8000-000000000001","effect":"require","conditions":[{"field":"F1","op":"isNotEmpty"}]}"""),
            Choice("30000000-0000-4000-8000-000000000005", """{"items":[{"value":"am","places":5}]}""", rule: """{"key":"40000000-0000-4000-8000-000000000002","effect":"require","enabled":false,"conditions":[{"field":"F1","op":"isNotEmpty"}]}"""),
            Choice("30000000-0000-4000-8000-000000000006", """{"items":[{"value":"am","places":5}]}""", required: true, settings: """{"other":true}"""),
            Choice("30000000-0000-4000-8000-000000000007", $$$"""{"source":"list","list":"{{{full}}}"}""", required: true),
            Choice("30000000-0000-4000-8000-000000000008", $$$"""{"source":"list","list":"{{{open}}}"}""", required: true),
            Choice("30000000-0000-4000-8000-000000000009", """{"source":"list","list":"70000000-0000-4000-8000-000000000009"}""", required: true));

        var issues = await new FullOptionsFormCheck(Types, site.Lists).CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, FullOptionsFormCheck.OptionsCanFill, Fields + "[0].options"),
            (FormCheckSeverity.Warning, FullOptionsFormCheck.OptionsCanFill, Fields + "[3].options"),
            (FormCheckSeverity.Warning, FullOptionsFormCheck.OptionsCanFill, Fields + "[6].options"),
        }));
    }

    [Test]
    public async Task AFormThatKeepsNoEntries_WarnsAboutEachLimitThatCountsThem()
    {
        var site = new OptionListTestSite();
        var definition = Form(
            [
                Choice("F1", """{"items":[{"value":"am","places":5},{"value":"pm"}]}"""),
                """{"key":"F2","type":"email","alias":"mail","label":{"":"Mail"},"validation":[{"kind":"uniquePerForm"}]}""",
                Choice("F3", """{"items":[{"value":"am"}]}"""),
            ],
            ""","settings":{"submission":{"store":"never"},"availability":{"maxEntries":100,"onePerMember":true}}""");

        var issues = await new KeptLimitsFormCheck(Types, site.Lists).CheckAsync(definition, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
            {
                (FormCheckSeverity.Warning, KeptLimitsFormCheck.LimitWithoutEntries, "$.settings.availability.maxEntries"),
                (FormCheckSeverity.Warning, KeptLimitsFormCheck.LimitWithoutEntries, "$.settings.availability.onePerMember"),
                (FormCheckSeverity.Warning, KeptLimitsFormCheck.LimitWithoutEntries, Fields + "[0].options"),
                (FormCheckSeverity.Warning, KeptLimitsFormCheck.LimitWithoutEntries, Fields + "[1].validation[0]"),
            }));
            Assert.That(issues[0].Message, Does.StartWith("The form keeps no entries"));
        });
    }

    [Test]
    public async Task AFieldKeptOnlyUntilTheActionsRun_WarnsAboutItsPlacesAndUniqueAnswers_AndAFormThatKeepsEntriesForDaysDoesNot()
    {
        var site = new OptionListTestSite();
        var definition = Form(
            [
                Choice("F1", """{"items":[{"value":"am","places":5}]}""", keep: "untilActionsRun"),
                """{"key":"F2","type":"email","alias":"mail","label":{"":"Mail"},"keep":"untilActionsRun","validation":[{"kind":"uniquePerForm"}]}""",
                """{"key":"F3","type":"email","alias":"other","label":{"":"Other"},"validation":[{"kind":"uniquePerForm"}]}""",
            ],
            ""","settings":{"submission":{"store":"days","storeDays":30},"availability":{"maxEntries":100}}""");

        var issues = await new KeptLimitsFormCheck(Types, site.Lists).CheckAsync(definition, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues.Select(issue => issue.Path), Is.EqualTo(new[] { Fields + "[0].options", Fields + "[1].validation[0]" }));
            Assert.That(issues.Select(issue => issue.Message), Has.All.StartWith("This field's answers are kept only until the actions have run"));
        });
    }

    [Test]
    public async Task ALastOutcomeWithARule_IsAWarning_AndOneSwitchedOffIsNone()
    {
        const string Rule = """{"key":"40000000-0000-4000-8000-00000000000K","effect":"use","enabled":E,"conditions":[{"field":"F1","op":"isNotEmpty"}]}""";
        string Outcomes(string first, string last) =>
            $$$""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","rules":[{{{first}}}]},{"key":"60000000-0000-4000-8000-000000000002","rules":[{{{last}}}]}]""";
        string RuleOf(int key, bool enabled) => Rule.Replace("K", key.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("E", enabled ? "true" : "false", StringComparison.Ordinal);
        async Task<IReadOnlyList<FormCheckIssue>> CheckAsync(string rest) => await new LastOutcomeFormCheck().CheckAsync(Form([Text("F1", "Topic")], rest), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((await CheckAsync(Outcomes(RuleOf(1, true), RuleOf(2, true)))).Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Warning, LastOutcomeFormCheck.LastOutcomeHasRule, "$.outcomes[1].rules[0]") }));
            Assert.That(await CheckAsync(Outcomes(RuleOf(1, true), RuleOf(2, false))), Is.Empty, "a rule switched off");
            Assert.That(await CheckAsync(Outcomes(RuleOf(1, true), "")), Is.Empty, "the last without a rule");
            Assert.That(await CheckAsync(""), Is.Empty, "no outcomes");
        });
    }

    private static string Text(string key, string label, string? autocomplete = null) =>
        $$$"""{"key":"{{{key}}}","type":"text","alias":"{{{AliasOf("a", key)}}}","label":{"":"{{{label}}}"}{{{(autocomplete is null ? "" : $",\"autocomplete\":\"{autocomplete}\"")}}}}""";

    private static string Email(string key) => $$$"""{"key":"{{{key}}}","type":"email","alias":"mail","label":{"":"Email"}}""";

    private static string Choice(string key, string options, bool required = false, string? rule = null, string? settings = null, string? keep = null) =>
        $$$"""{"key":"{{{key}}}","type":"radio","alias":"{{{AliasOf("c", key)}}}","label":{"":"Choice"}{{{(required ? ",\"required\":true" : "")}}}{{{(keep is null ? "" : $",\"keep\":\"{keep}\"")}}}{{{(rule is null ? "" : $",\"rules\":[{rule}]")}}},"options":{{{options}}}{{{(settings is null ? "" : $",\"settings\":{settings}")}}}}""";

    // An alias of its own for each key, short or long.
    private static string AliasOf(string start, string key) => start + key.Replace("-", "", StringComparison.Ordinal);

    private static FormDefinition Form(params string[] fields) => Form(fields, "");

    // A form of one page and group with these fields, keys F1 to F4 written short, and anything after the pages.
    private static FormDefinition Form(string[] fields, string rest)
    {
        var pages = """[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[""" + string.Join(",", fields) + "]}]}]";
        foreach (var short_ in new[] { "F1", "F2", "F3", "F4" })
        {
            var key = $"30000000-0000-4000-8000-00000000000{short_[1]}";
            pages = pages.Replace($"\"{short_}\"", $"\"{key}\"", StringComparison.Ordinal);
            rest = rest.Replace($"\"{short_}\"", $"\"{key}\"", StringComparison.Ordinal);
        }

        return DefinitionFixtures.Read("{\"schemaVersion\":1,\"key\":\"f0000000-0000-4000-8000-000000000001\",\"pages\":" + pages + rest + "}");
    }

    /// <summary>A site's own type whose answers are about a person by their nature.</summary>
    public sealed class PassportType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.passport",
        Name = "Passport",
        Icon = "icon-user",
        PaletteGroup = FieldPaletteGroup.People,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Text,
        Features = FieldFeatures.Label | FieldFeatures.Required | FieldFeatures.VisibilityRules,
        Messages = FieldMessageKinds.Required,
        PersonalData = true,
    });
}
