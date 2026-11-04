using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The form check on the server (ROADMAP 9.5b, ADR-0177): every text where merge tags render with its place, context,
/// owner and path; the merge tags' errors and warnings, a redirect that recalls answers, a field without a label, a
/// captcha provider without its keys, an email action without an email field to reply to, an action type's own warnings;
/// and the check's order of errors, warnings and hints over the form as the site runs it, with a warning where a host's
/// lock replaces a value the form stores (9.5e).
/// </summary>
[TestFixture]
public sealed class FormCheckTests
{
    private const string Field1 = "30000000-0000-4000-8000-000000000001";
    private const string Field2 = "30000000-0000-4000-8000-000000000002";

    private static readonly IFormBuilderMergeTagService Tags = MergeTagTestSite.Create().Service;

    private static readonly FieldTypeRegistry Types = TestFieldTypes.Build(builder => builder.Append<CardType>());

    private static readonly ActionTypeRegistry Actions = new([new NoteType()]);

    [Test]
    public void EveryTemplate_IsFound_WithItsPlaceContextOwnerAndPath()
    {
        var definition = Read(
            """
            [{"key":"10000000-0000-4000-8000-000000000001","title":{"":"Page","de":"Seite"},"groups":[{"key":"20000000-0000-4000-8000-000000000001","description":{"":"<p>About</p>"},"fields":[
              {"key":"F1","type":"text","alias":"name","label":{"":"Name"},"help":{"":"Help"},"placeholder":{"":"Ann"},"defaultValue":"{{ form.name }}"},
              {"key":"F2","type":"hidden","alias":"source","defaultValue":["a","b"]},
              {"key":"30000000-0000-4000-8000-000000000003","type":"consent","alias":"ok","label":{"":"OK"},"settings":{"text":{"":"I agree"}}},
              {"key":"30000000-0000-4000-8000-000000000004","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"30000000-0000-4000-8000-000000000005","type":"text","alias":"kid","label":{"":"Kid"}}]},
              {"key":"30000000-0000-4000-8000-000000000006","type":"heading","settings":{"text":{"":"Welcome"},"level":2}},
              {"key":"30000000-0000-4000-8000-000000000007","type":"paragraph","settings":{"html":{"":"<p>Hi</p>"}}},
              {"key":"30000000-0000-4000-8000-000000000008","type":"test.card","settings":{"caption":{"text":"Once"}}}]}]}]
            """,
            ""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","kind":"redirect","message":{"":"<p>Thanks</p>"},"redirect":{"url":"/thanks"}}],"settings":{"submission":{"entryTitle":"{{ field.name }}"}},"actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note","settings":{"note":"{{ field.name }}"}}]""");

        var found = FormTemplates.Of(definition, Types);

        var page = "$.pages[0]";
        var fields = page + ".groups[0].fields";
        Guid Key(int last) => Guid.Parse($"30000000-0000-4000-8000-00000000000{last}");
        Assert.That(found.Select(template => (template.Place, template.Context, template.Owner, template.Path)), Is.EqualTo(new[]
        {
            (MergeTagPlace.VisitorText, MergeTagContext.Text, (Guid?)definition.Pages[0].Key, page + ".title['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, definition.Pages[0].Key, page + ".title.de"),
            (MergeTagPlace.VisitorText, MergeTagContext.Html, definition.Pages[0].Groups[0].Key, page + ".groups[0].description['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(1), fields + "[0].label['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(1), fields + "[0].help['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(1), fields + "[0].placeholder['']"),
            (MergeTagPlace.DefaultValue, MergeTagContext.Text, Key(1), fields + "[0].defaultValue"),
            // A value kept on the server renders there, each of a list's values on its own.
            (MergeTagPlace.HiddenValue, MergeTagContext.Text, Key(2), fields + "[1].defaultValue[0]"),
            (MergeTagPlace.HiddenValue, MergeTagContext.Text, Key(2), fields + "[1].defaultValue[1]"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(3), fields + "[2].label['']"),
            (MergeTagPlace.ConsentText, MergeTagContext.Html, Key(3), fields + "[2].settings.text['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(4), fields + "[3].label['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(5), fields + "[3].fields[0].label['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(6), fields + "[4].settings.text['']"),
            (MergeTagPlace.VisitorText, MergeTagContext.Html, Key(7), fields + "[5].settings.html['']"),
            // A nested setting's text, written as one text rather than one per culture.
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Key(8), fields + "[6].settings.caption.text"),
            (MergeTagPlace.Outcome, MergeTagContext.Html, (Guid?)null, "$.outcomes[0].message['']"),
            (MergeTagPlace.Redirect, MergeTagContext.Url, null, "$.outcomes[0].redirect.url"),
            (MergeTagPlace.EntryTitle, MergeTagContext.Text, null, "$.settings.submission.entryTitle"),
        }));
    }

    [Test]
    public void ATypeOrAnActionTheSiteLacks_IsPassedOver_ForTheStructuralValidationToRefuse()
    {
        var definition = Read(
            """[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"gone.type","alias":"x","label":{"":"X"},"settings":{"text":"{{ nothing.here }}"}}]}]}]""",
            ""","actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"gone.action","settings":{"note":"x"}}]""");

        Assert.That(FormTemplates.Of(definition, Types).Select(template => template.Path), Is.EqualTo(new[] { "$.pages[0].groups[0].fields[0].label['']" }));
    }

    [Test]
    public async Task MergeTags_AreErrorsOrWarningsAsTheyRender()
    {
        var definition = Read(
            """
            [{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[
              {"key":"F1","type":"text","alias":"name","label":{"":"Hi {{ field.later }}"},"help":{"":"{{ nothing.here }}"}},
              {"key":"F2","type":"text","alias":"later","label":{"":"Later {{ form.name"}},
              {"key":"30000000-0000-4000-8000-000000000003","type":"consent","alias":"ok","label":{"":"OK"},"settings":{"text":{"":"I, {{ field.name }}, agree"}}}]}]}]
            """,
            ""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","kind":"redirect","redirect":{"url":"/thanks?who={{ field.name }}"}}]""");

        var issues = await new MergeTagFormCheck(Tags, Types).CheckAsync(definition, CancellationToken.None);

        var fields = "$.pages[0].groups[0].fields";
        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Error, MergeTagProblems.FieldAfterText, fields + "[0].label['']"),
            (FormCheckSeverity.Warning, MergeTagProblems.UnknownPath, fields + "[0].help['']"),
            (FormCheckSeverity.Error, "unclosed_tag", fields + "[1].label['']"),
            (FormCheckSeverity.Error, MergeTagProblems.SourceNotForPlace, fields + "[2].settings.text['']"),
            (FormCheckSeverity.Warning, MergeTagFormCheck.AnswersInUrl, "$.outcomes[0].redirect.url"),
        }));
        Assert.That(issues[0].Message, Is.EqualTo("The merge tag at character 7 recalls the answer of a field that comes after this text."));
    }

    [Test]
    public void ARedirect_RecallsAnswers_WhenItNamesAFieldsValueOrTheEntrysAnswers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?b={{ field.email }}"), Is.True);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?b={{ field.address.city }}"), Is.True);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?all={{ entry.answers }}"), Is.True);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?all={{ entry.table }}"), Is.True);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?r={{ entry.reference }}&n={{ entry.number }}&s={{ entry.state }}"), Is.False, "no answer");
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?l={{ field.email.label }}"), Is.False, "a field's label is no answer");
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?f={{ form.alias }}&p={{ page.url }}"), Is.False);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?f=field.email"), Is.False);
        });
    }

    [Test]
    public async Task AFieldVisitorsSee_WithoutALabel_IsAnError_AndContentHiddenValuesAConsentAndAnUnseenCalculationNeedNone()
    {
        var definition = Read(
            """
            [{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[
              {"key":"F1","type":"text","alias":"name"},
              {"key":"F2","type":"text","alias":"blank","label":{"":"  ","de":"Name"}},
              {"key":"30000000-0000-4000-8000-000000000003","type":"heading","settings":{"text":{"":"Hello"}}},
              {"key":"30000000-0000-4000-8000-000000000004","type":"hidden","alias":"source"},
              {"key":"30000000-0000-4000-8000-000000000007","type":"consent","alias":"agreed","settings":{"text":{"":"I agree"}}},
              {"key":"30000000-0000-4000-8000-000000000008","type":"calculation","alias":"total","settings":{"formula":"1","visible":false}},
              {"key":"30000000-0000-4000-8000-000000000009","type":"calculation","alias":"shown","settings":{"formula":"1","visible":true}},
              {"key":"30000000-0000-4000-8000-000000000005","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"30000000-0000-4000-8000-000000000006","type":"text","alias":"kid"}]}]}]}]
            """);

        var issues = await new LabelFormCheck(Types).CheckAsync(definition, CancellationToken.None);

        var fields = "$.pages[0].groups[0].fields";
        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[0].label"),
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[1].label"),
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[6].label"),
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[7].fields[0].label"),
        }));
    }

    [Test]
    public async Task ACaptchaProviderWithoutItsKeys_IsAWarning_AndOneWithKeysOrNoneTheSiteHasIsNot()
    {
        var providers = new FormBuilderCaptchaProviderCollection(() => [new Provider("keyless", false), new Provider("keyed", true)]);
        var check = new CaptchaKeysFormCheck(providers);

        var keyless = await check.CheckAsync(WithCaptcha("keyless"), CancellationToken.None);
        var keyed = await check.CheckAsync(WithCaptcha("keyed"), CancellationToken.None);
        var unknown = await check.CheckAsync(WithCaptcha("gone"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(keyless.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Warning, CaptchaKeysFormCheck.CaptchaNotConfigured, "$.settings.protection.captcha.provider") }));
            Assert.That(keyed, Is.Empty);
            Assert.That(unknown, Is.Empty, "the structural validation's error");
        });
    }

    [Test]
    public async Task AnEmailAction_WithoutAnEmailFieldToReplyTo_IsAWarning_OrAHintWhereTheFormHasOne()
    {
        var check = new EmailReplyFormCheck();
        const string Text = """[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"text","alias":"name","label":{"":"Name"}}]}]}]""";
        const string Nested = """[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"F2","type":"email","alias":"mail","label":{"":"Mail"}}]}]}]}]""";
        const string Mails = ""","actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note"},{"key":"50000000-0000-4000-8000-000000000002","type":"email"},{"key":"50000000-0000-4000-8000-000000000003","type":"email","enabled":false},""" +
            """{"key":"50000000-0000-4000-8000-000000000004","type":"email","settings":{"replyToField":"30000000-0000-4000-8000-000000000002"}},{"key":"50000000-0000-4000-8000-000000000005","type":"email","settings":{"toField":"30000000-0000-4000-8000-000000000002"}},""" +
            // A reply field that is no field's key, which the action's own check refuses, names none; a null one is left out on reading.
            """{"key":"50000000-0000-4000-8000-000000000006","type":"email","settings":{"replyToField":5}}]""";

        var without = await check.CheckAsync(Read(Text, Mails), CancellationToken.None);
        var with = await check.CheckAsync(Read(Nested, Mails), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(without.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
            {
                (FormCheckSeverity.Warning, EmailReplyFormCheck.NoReplyField, "$.actions[1]"),
                (FormCheckSeverity.Warning, EmailReplyFormCheck.NoReplyField, "$.actions[3]"),
                (FormCheckSeverity.Warning, EmailReplyFormCheck.NoReplyField, "$.actions[4]"),
                (FormCheckSeverity.Warning, EmailReplyFormCheck.NoReplyField, "$.actions[5]"),
            }));
            // A repeatable group's email field is one too; a mail that names one to reply to, or goes to the visitor, needs nothing.
            Assert.That(with.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
            {
                (FormCheckSeverity.Hint, EmailReplyFormCheck.ReplyFieldUnset, "$.actions[1].settings.replyToField"),
                (FormCheckSeverity.Hint, EmailReplyFormCheck.ReplyFieldUnset, "$.actions[5].settings.replyToField"),
            }));
        });
    }

    [Test]
    public async Task AnActionTypesOwnCheck_RefusesWithErrors_AndWarnsWithWarnings_AtTheirPaths()
    {
        var check = new ActionPublishCheck(Actions);
        var definition = Read("[]", ""","actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note","settings":{"note":"?"}},{"key":"50000000-0000-4000-8000-000000000002","type":"test.note","settings":{"note":"!"}},{"key":"50000000-0000-4000-8000-000000000003","type":"gone"},""" +
            """{"key":"50000000-0000-4000-8000-000000000004","type":"test.note","settings":{"note":5}}]""");

        var issues = await check.CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Error, DefinitionErrors.SettingInvalid, "$.actions[1].settings.note"),
            (FormCheckSeverity.Error, DefinitionErrors.UnknownType, "$.actions[2].type"),
            // Settings that break the type's schema are refused by it alone: the type's own check never reads them.
            (FormCheckSeverity.Error, DefinitionErrors.SettingInvalid, "$.actions[3].settings.note"),
            (FormCheckSeverity.Warning, "note_unsure", "$.actions[0].settings.note"),
        }));
    }

    [Test]
    public void AWarning_NamesItsPlaceAndItsCode_AsTheyAreWritten()
    {
        var check = new ActionSettingsCheck(new FormAction { Key = Guid.NewGuid(), Type = "test.note" }, Read("[]"), "$.actions[0].settings", new DefinitionProblems());

        check.Warn("headers[2]", "a_code", "A sentence.");

        Assert.Multiple(() =>
        {
            Assert.That(() => check.Warn("not a place", "a_code", "A sentence."), Throws.ArgumentException);
            Assert.That(() => check.Warn("note", "Not-A-Code", "A sentence."), Throws.ArgumentException);
            Assert.That(() => check.Warn("note", string.Empty, "A sentence."), Throws.ArgumentException);
            Assert.That(() => check.Warn("note", "a_code", " "), Throws.ArgumentException);
            Assert.That(check.Warnings, Is.EqualTo(new[] { ("a_code", "$.actions[0].settings.headers[2]", "A sentence.") }));
        });
    }

    [Test]
    public async Task TheCheck_ListsErrorsThenWarningsThenHints_ThePublishChecksAmongItsErrors()
    {
        var problem = new DefinitionProblem("alias_duplicate", "$.pages[0].groups[0].fields[1].alias", "Twice.");
        var formCheck = FormCheckOf(
            [new Publishing([problem])],
            [
                new Weighing([Issue(FormCheckSeverity.Hint, "h"), Issue(FormCheckSeverity.Warning, "w1"), Issue(FormCheckSeverity.Error, "e")]),
                new Weighing([Issue(FormCheckSeverity.Warning, "w2")]),
            ]);

        var issues = await formCheck.CheckAsync(Read("[]"), CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Error, "alias_duplicate"),
            (FormCheckSeverity.Error, "e"),
            (FormCheckSeverity.Warning, "w1"),
            (FormCheckSeverity.Warning, "w2"),
            (FormCheckSeverity.Hint, "h"),
        }));
    }

    [Test]
    public async Task TheCheck_SeesTheFormAsTheSiteRunsIt_WithTheHostsLocks()
    {
        var options = new FormBuilderOptions();
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = DesignerSettingLock.FieldKind, Type = "text", Setting = "maxLength", Value = "9" });
        var seen = new Recording();
        var formCheck = new FormCheck(
            [],
            [seen],
            new FieldSettingLocks(Options.Create(options), Types),
            new ActionSettingLocks(Options.Create(new FormBuilderOptions()), Actions));

        _ = await formCheck.CheckAsync(Read("""[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"text","alias":"name","label":{"":"Name"},"settings":{"maxLength":200}}]}]}]"""), CancellationToken.None);

        Assert.That(seen.Definition?.Pages[0].Groups[0].Fields[0].Settings?.Value.GetProperty("maxLength").GetRawText(), Is.EqualTo("9"));
    }

    [Test]
    public async Task AHostsLock_ThatReplacesAValueTheFormStores_IsAWarning_WhereItIsStored_UnlessTheLockIsHidden()
    {
        var options = new FormBuilderOptions();
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = DesignerSettingLock.FieldKind, Type = "text", Setting = "maxLength", Value = "9" });
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = DesignerSettingLock.FieldKind, Type = "text", Setting = "prefix", Value = "+", Hidden = true });
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = DesignerSettingLock.FieldKind, Type = "text", Setting = "suffix" });
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = DesignerSettingLock.ActionKind, Type = "test.note", Setting = "note", Value = "Kept" });
        var formCheck = new FormCheck([], [], new FieldSettingLocks(Options.Create(options), Types), new ActionSettingLocks(Options.Create(options), Actions));
        var definition = Read(
            """
            [{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[
              {"key":"F1","type":"text","alias":"name","label":{"":"Name"},"settings":{"maxLength":200,"prefix":"x","suffix":"kg"}},
              {"key":"F2","type":"text","alias":"same","label":{"":"Same"},"settings":{"maxLength":9}},
              {"key":"30000000-0000-4000-8000-000000000003","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"30000000-0000-4000-8000-000000000004","type":"text","alias":"kid","label":{"":"Kid"},"settings":{"maxLength":20}}]}]}]}]
            """,
            ""","actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note","settings":{"note":"Mine"}},{"key":"50000000-0000-4000-8000-000000000002","type":"test.note","settings":{"note":"Kept"}}]""");

        var issues = await formCheck.CheckAsync(definition, CancellationToken.None);

        var fields = "$.pages[0].groups[0].fields";
        Assert.Multiple(() =>
        {
            Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
            {
                (FormCheckSeverity.Warning, FormCheck.LockReplacesValue, fields + "[0].settings.maxLength"),
                // A lock without a value keeps the setting's default, which replaces the field's own.
                (FormCheckSeverity.Warning, FormCheck.LockReplacesValue, fields + "[0].settings.suffix"),
                (FormCheckSeverity.Warning, FormCheck.LockReplacesValue, fields + "[2].fields[0].settings.maxLength"),
                (FormCheckSeverity.Warning, FormCheck.LockReplacesValue, "$.actions[0].settings.note"),
            }));
            Assert.That(issues[0].Message, Is.EqualTo("The site locks this setting (JironFormBuilder:Designer:SettingLocks:0), so the form runs with the locked value instead of the one it stores."));
        });
    }

    [Test]
    public async Task AMergeTagInALabel_FromASourceALabelRefuses_IsAnError()
    {
        var definition = Read("""[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"text","alias":"name","label":{"":"Hello {{ member.name }}"}}]}]}]""");

        var issues = await new MergeTagFormCheck(Tags, Types).CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Error, MergeTagProblems.SourceNotForPlace, "$.pages[0].groups[0].fields[0].label['']") }));
    }

    [Test]
    public async Task TheCheck_KeepsAtMostAHundredOfEachWeight()
    {
        var many = Enumerable.Range(0, 150).SelectMany(at => new[] { Issue(FormCheckSeverity.Error, $"e{at}"), Issue(FormCheckSeverity.Warning, $"w{at}"), Issue(FormCheckSeverity.Hint, $"h{at}") }).ToList();

        var issues = await FormCheckOf([], [new Weighing(many)]).CheckAsync(Read("[]"), CancellationToken.None);

        Assert.That(new[] { FormCheckSeverity.Error, FormCheckSeverity.Warning, FormCheckSeverity.Hint }.Select(severity => issues.Count(issue => issue.Severity == severity)), Is.EqualTo(new[] { 100, 100, 100 }));
    }

    private static FormCheck FormCheckOf(IEnumerable<IFormPublishCheck> publishChecks, IEnumerable<IFormCheck> checks) => new(
        publishChecks,
        checks,
        new FieldSettingLocks(Options.Create(new FormBuilderOptions()), Types),
        new ActionSettingLocks(Options.Create(new FormBuilderOptions()), Actions));

    private static FormCheckIssue Issue(FormCheckSeverity severity, string code) => new(severity, code, "$", code);

    private static FormDefinition WithCaptcha(string provider) =>
        Read("[]", ",\"settings\":{\"protection\":{\"captcha\":{\"provider\":\"" + provider + "\"}}}");

    // A definition of the pages given, field keys F1 and F2 written short, and anything after the pages.
    private static FormDefinition Read(string pages, string rest = "") =>
        DefinitionFixtures.Read(
            "{\"schemaVersion\":1,\"key\":\"f0000000-0000-4000-8000-000000000001\",\"pages\":"
            + pages.Replace("\"F1\"", $"\"{Field1}\"", StringComparison.Ordinal).Replace("\"F2\"", $"\"{Field2}\"", StringComparison.Ordinal)
            + rest + "}");

    /// <summary>A site's own type whose settings hold a nested object with a text that takes merge tags.</summary>
    public sealed class CardType() : FormBuilderFieldTypeBase<CardSettings>(new FieldTypeDeclaration
    {
        Alias = "test.card",
        Name = "Card",
        Icon = "icon-settings",
        PaletteGroup = FieldPaletteGroup.Content,
        Features = FieldFeatures.VisibilityRules,
    });

    /// <summary>The card's settings.</summary>
    public sealed class CardSettings
    {
        /// <summary>Its caption.</summary>
        [FormBuilderSetting("Caption", Kind = FieldSettingKind.Nested)]
        public CardCaption? Caption { get; init; }
    }

    /// <summary>The card's caption.</summary>
    public sealed class CardCaption
    {
        /// <summary>Its text.</summary>
        [FormBuilderSetting("Text", SupportsMergeTags = true)]
        public string? Text { get; init; }
    }

    private sealed class Publishing(IReadOnlyList<DefinitionProblem> problems) : IFormPublishCheck
    {
        public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken) => ValueTask.FromResult(problems);
    }

    private sealed class Recording : IFormCheck
    {
        public FormDefinition? Definition { get; private set; }

        public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
        {
            Definition = definition;
            return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>([]);
        }
    }

    private sealed class Weighing(IReadOnlyList<FormCheckIssue> issues) : IFormCheck
    {
        public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken) => ValueTask.FromResult(issues);
    }

    private sealed class Provider(string name, bool configured) : IFormBuilderCaptchaProvider
    {
        public string Name => name;

        public bool IsConfigured => configured;

        public FormBuilderCaptchaClient? Client => null;

        public Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoteSettings
    {
        [FormBuilderSetting("Note")]
        public string? Note { get; init; }
    }

    // An action type whose own check refuses a note of "!", is unsure of one of "?" and misses one it cannot read.
    private sealed class NoteType() : FormBuilderActionTypeBase<NoteSettings>(new ActionTypeDeclaration { Alias = "test.note", Name = "Note", Icon = "icon-note" })
    {
        protected override void CheckSettings(NoteSettings settings, ActionSettingsCheck check)
        {
            if (settings.Note is null)
            {
                check.Warn("note", "note_missing", "A note says something.");
            }
            else if (settings.Note == "!")
            {
                check.Invalid("note", "A note is more than a bang.");
            }
            else if (settings.Note == "?")
            {
                check.Warn("note", "note_unsure", "A note of a question mark may puzzle its reader.");
            }
        }

        protected override Task<ActionResult> RunAsync(NoteSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }
}
