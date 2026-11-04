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
/// captcha provider without its keys, an email action without an email field to reply to; and the check's order of
/// errors, warnings and hints, the publish checks' problems among its errors.
/// </summary>
[TestFixture]
public sealed class FormCheckTests
{
    private const string Field1 = "30000000-0000-4000-8000-000000000001";
    private const string Field2 = "30000000-0000-4000-8000-000000000002";

    private static readonly IFormBuilderMergeTagService Tags = MergeTagTestSite.Create().Service;

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
              {"key":"30000000-0000-4000-8000-000000000004","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"30000000-0000-4000-8000-000000000005","type":"text","alias":"kid","label":{"":"Kid"}}]}]}]}]
            """,
            ""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","kind":"redirect","message":{"":"<p>Thanks</p>"},"redirect":{"url":"/thanks"}}],"settings":{"submission":{"entryTitle":"{{ field.name }}"}},"actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note","settings":{"note":"{{ field.name }}"}}]""");

        var found = FormTemplates.Of(definition, TestFieldTypes.Registry, Actions);

        var page = "$.pages[0]";
        var fields = page + ".groups[0].fields";
        Assert.That(found.Select(template => (template.Place, template.Context, template.Owner, template.Path, template.OfAction)), Is.EqualTo(new[]
        {
            (MergeTagPlace.VisitorText, MergeTagContext.Text, (Guid?)definition.Pages[0].Key, page + ".title['']", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, definition.Pages[0].Key, page + ".title.de", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Html, definition.Pages[0].Groups[0].Key, page + ".groups[0].description['']", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Guid.Parse(Field1), fields + "[0].label['']", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Guid.Parse(Field1), fields + "[0].help['']", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Guid.Parse(Field1), fields + "[0].placeholder['']", false),
            (MergeTagPlace.DefaultValue, MergeTagContext.Text, Guid.Parse(Field1), fields + "[0].defaultValue", false),
            // A value kept on the server renders there, each of a list's values on its own.
            (MergeTagPlace.HiddenValue, MergeTagContext.Text, Guid.Parse(Field2), fields + "[1].defaultValue[0]", false),
            (MergeTagPlace.HiddenValue, MergeTagContext.Text, Guid.Parse(Field2), fields + "[1].defaultValue[1]", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Guid.Parse("30000000-0000-4000-8000-000000000003"), fields + "[2].label['']", false),
            (MergeTagPlace.ConsentText, MergeTagContext.Html, Guid.Parse("30000000-0000-4000-8000-000000000003"), fields + "[2].settings.text['']", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Guid.Parse("30000000-0000-4000-8000-000000000004"), fields + "[3].label['']", false),
            (MergeTagPlace.VisitorText, MergeTagContext.Text, Guid.Parse("30000000-0000-4000-8000-000000000005"), fields + "[3].fields[0].label['']", false),
            (MergeTagPlace.Outcome, MergeTagContext.Html, (Guid?)null, "$.outcomes[0].message['']", false),
            (MergeTagPlace.Redirect, MergeTagContext.Url, null, "$.outcomes[0].redirect.url", false),
            (MergeTagPlace.EntryTitle, MergeTagContext.Text, null, "$.settings.submission.entryTitle", false),
            (MergeTagPlace.Action, MergeTagContext.Text, null, "$.actions[0].settings.note", true),
        }));
    }

    [Test]
    public async Task MergeTags_AreErrorsOrWarningsAsTheyRender_AndAnActionsErrorsAreItsTypes()
    {
        var definition = Read(
            """
            [{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[
              {"key":"F1","type":"text","alias":"name","label":{"":"Hi {{ field.later }}"},"help":{"":"{{ nothing.here }}"}},
              {"key":"F2","type":"text","alias":"later","label":{"":"Later {{ form.name"}},
              {"key":"30000000-0000-4000-8000-000000000003","type":"consent","alias":"ok","label":{"":"OK"},"settings":{"text":{"":"I, {{ field.name }}, agree"}}}]}]}]
            """,
            ""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","kind":"redirect","redirect":{"url":"/thanks?who={{ field.name }}"}}],"actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note","settings":{"note":"{{ nobody.knows }} {{ field.name | nope }"}}]""");

        var issues = await new MergeTagFormCheck(Tags, TestFieldTypes.Registry, Actions).CheckAsync(definition, CancellationToken.None);

        var fields = "$.pages[0].groups[0].fields";
        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Error, MergeTagProblems.FieldAfterText, fields + "[0].label['']"),
            (FormCheckSeverity.Warning, MergeTagProblems.UnknownPath, fields + "[0].help['']"),
            (FormCheckSeverity.Error, "unclosed_tag", fields + "[1].label['']"),
            (FormCheckSeverity.Error, MergeTagProblems.SourceNotForPlace, fields + "[2].settings.text['']"),
            (FormCheckSeverity.Warning, MergeTagFormCheck.AnswersInUrl, "$.outcomes[0].redirect.url"),
            (FormCheckSeverity.Warning, MergeTagProblems.UnknownPath, "$.actions[0].settings.note"),
        }));
        Assert.That(issues[0].Message, Is.EqualTo("The merge tag at character 7 recalls the answer of a field that comes after this text."));
    }

    [Test]
    public void ARedirect_RecallsAnswers_WhenItNamesAFieldOrTheEntry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?b={{ field.email }}"), Is.True);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?r={{ entry.reference }}"), Is.True);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?f={{ form.alias }}&p={{ page.url }}"), Is.False);
            Assert.That(MergeTagFormCheck.RecallsAnswers("/a?f=field.email"), Is.False);
        });
    }

    [Test]
    public async Task AFieldVisitorsSee_WithoutALabel_IsAnError_AndContentAValueKeptOnTheServerAndAConsentNeedNone()
    {
        var definition = Read(
            """
            [{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[
              {"key":"F1","type":"text","alias":"name"},
              {"key":"F2","type":"text","alias":"blank","label":{"":"  ","de":"Name"}},
              {"key":"30000000-0000-4000-8000-000000000003","type":"heading","settings":{"text":{"":"Hello"}}},
              {"key":"30000000-0000-4000-8000-000000000004","type":"hidden","alias":"source"},
              {"key":"30000000-0000-4000-8000-000000000007","type":"consent","alias":"agreed","settings":{"text":{"":"I agree"}}},
              {"key":"30000000-0000-4000-8000-000000000005","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"30000000-0000-4000-8000-000000000006","type":"text","alias":"kid"}]}]}]}]
            """);

        var issues = await new LabelFormCheck(TestFieldTypes.Registry).CheckAsync(definition, CancellationToken.None);

        var fields = "$.pages[0].groups[0].fields";
        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[0].label"),
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[1].label"),
            (FormCheckSeverity.Error, LabelFormCheck.LabelMissing, fields + "[5].fields[0].label"),
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
    public async Task AnEmailAction_OnAFormWithoutAnEmailField_IsAWarning_UnlessSwitchedOff()
    {
        var check = new EmailReplyFormCheck();
        const string Text = """[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"text","alias":"name","label":{"":"Name"}}]}]}]""";
        const string Nested = """[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"repeater","alias":"kids","label":{"":"Kids"},"fields":[{"key":"F2","type":"email","alias":"mail","label":{"":"Mail"}}]}]}]}]""";
        const string Actions = ""","actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.note"},{"key":"50000000-0000-4000-8000-000000000002","type":"email"},{"key":"50000000-0000-4000-8000-000000000003","type":"email","enabled":false}]""";

        var without = await check.CheckAsync(Read(Text, Actions), CancellationToken.None);
        var nested = await check.CheckAsync(Read(Nested, Actions), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(without.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Warning, EmailReplyFormCheck.NoReplyField, "$.actions[1]") }));
            Assert.That(nested, Is.Empty, "a repeatable group's email field is one too");
        });
    }

    [Test]
    public async Task TheCheck_ListsErrorsThenWarningsThenHints_ThePublishChecksAmongItsErrors()
    {
        var problem = new DefinitionProblem("alias_duplicate", "$.pages[0].groups[0].fields[1].alias", "Twice.");
        var formCheck = new FormCheck(
            [new Publishing([problem])],
            [
                new Weighing([Issue(FormCheckSeverity.Hint, "h"), Issue(FormCheckSeverity.Warning, "w1"), Issue(FormCheckSeverity.Error, "e")]),
                new Weighing([Issue(FormCheckSeverity.Warning, "w2")]),
            ],
            new FieldSettingLocks(Options.Create(new FormBuilderOptions()), TestFieldTypes.Registry),
            new ActionSettingLocks(Options.Create(new FormBuilderOptions()), Actions));

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
            new FieldSettingLocks(Options.Create(options), TestFieldTypes.Registry),
            new ActionSettingLocks(Options.Create(new FormBuilderOptions()), Actions));

        _ = await formCheck.CheckAsync(Read("""[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"F1","type":"text","alias":"name","label":{"":"Name"},"settings":{"maxLength":200}}]}]}]"""), CancellationToken.None);

        Assert.That(seen.Definition?.Pages[0].Groups[0].Fields[0].Settings?.Value.GetProperty("maxLength").GetRawText(), Is.EqualTo("9"));
    }

    [Test]
    public async Task TheCheck_KeepsAtMostAHundredOfEachWeight()
    {
        var many = Enumerable.Range(0, 150).SelectMany(at => new[] { Issue(FormCheckSeverity.Error, $"e{at}"), Issue(FormCheckSeverity.Warning, $"w{at}"), Issue(FormCheckSeverity.Hint, $"h{at}") }).ToList();
        var formCheck = new FormCheck(
            [],
            [new Weighing(many)],
            new FieldSettingLocks(Options.Create(new FormBuilderOptions()), TestFieldTypes.Registry),
            new ActionSettingLocks(Options.Create(new FormBuilderOptions()), Actions));

        var issues = await formCheck.CheckAsync(Read("[]"), CancellationToken.None);

        Assert.That(new[] { FormCheckSeverity.Error, FormCheckSeverity.Warning, FormCheckSeverity.Hint }.Select(severity => issues.Count(issue => issue.Severity == severity)), Is.EqualTo(new[] { 100, 100, 100 }));
    }

    private static FormCheckIssue Issue(FormCheckSeverity severity, string code) => new(severity, code, "$", code);

    private static FormDefinition WithCaptcha(string provider) =>
        Read("[]", ",\"settings\":{\"protection\":{\"captcha\":{\"provider\":\"" + provider + "\"}}}");

    // A definition of the pages given, field keys F1 and F2 written short, and anything after the pages.
    private static FormDefinition Read(string pages, string rest = "") =>
        DefinitionFixtures.Read(
            "{\"schemaVersion\":1,\"key\":\"f0000000-0000-4000-8000-000000000001\",\"pages\":"
            + pages.Replace("\"F1\"", $"\"{Field1}\"", StringComparison.Ordinal).Replace("\"F2\"", $"\"{Field2}\"", StringComparison.Ordinal)
            + rest + "}");

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
        [FormBuilderSetting("Note", SupportsMergeTags = true)]
        public string? Note { get; init; }
    }

    private sealed class NoteType() : FormBuilderActionTypeBase<NoteSettings>(new ActionTypeDeclaration { Alias = "test.note", Name = "Note", Icon = "icon-note" })
    {
        protected override Task<ActionResult> RunAsync(NoteSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }
}
