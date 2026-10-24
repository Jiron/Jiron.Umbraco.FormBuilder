using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// A form saved before the host lowered a definition limit (ADR-0042, ADR-0044): it still opens, with the problem a save
/// would have, and a save or a publish of it is refused with <c>limit_exceeded</c> until the editor brings it within.
/// A duplicate, a template, an import or copied actions that would leave a form above the limit are refused the same way
/// (ADR-0046).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormServiceLimitTests : RepositoryTestBase
{
    /// <summary>Lowers the pages a definition may have to one.</summary>
    public FormServiceLimitTests() => InMemoryConfiguration[FormBuilderOptions.SectionName + ":Limits:Definitions:MaxPages"] = "1";

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    [Test]
    public async Task AFormAboveALoweredLimit_StillOpens_AndSavingItReturnsLimitExceeded()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var twoPages = blank with { Pages = [blank.Pages[0], new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }] };
        var row = (await Forms.GetAsync(created.Key))!;
        // The draft as it was saved while the host allowed more pages.
        await Forms.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Stored(twoPages), null, Editor);

        var draft = (await Service.GetDraftAsync(created.Key))!;

        Assert.Multiple(() =>
        {
            Assert.That(draft.Definition?.Pages, Has.Count.EqualTo(2), "it opens as it was stored");
            Assert.That(draft.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.pages") }));
            AssertCode(async () => await Service.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, draft.Definition!, null, Editor), "limit_exceeded", "$.pages");
            AssertCode(async () => await Service.PublishAsync(created.Key, draft.Form.ConcurrencyVersion, null, Editor), "limit_exceeded", "$.pages");
        });

        var within = await Service.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, draft.Definition! with { Pages = [draft.Definition!.Pages[0]] }, null, Editor);
        Assert.That(within?.Version, Is.EqualTo(3), "a definition within the limit saves");
    }

    [Test]
    public async Task AVersionAboveALoweredLimit_OpensWithItsProblem_AndRestoringItReturnsLimitExceeded()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var twoPages = blank with { Pages = [blank.Pages[0], new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }] };
        var row = (await Forms.GetAsync(created.Key))!;
        // Version 2 as it was saved while the host allowed more pages; version 3 is within the limit again.
        var older = (await Forms.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Stored(twoPages), null, Editor))!;
        var current = (await Forms.SaveDraftAsync(row.Id, older.Form.ConcurrencyVersion, Stored(blank), null, Editor))!;

        var opened = (await Service.GetVersionAsync(created.Key, 2))!;

        Assert.Multiple(async () =>
        {
            Assert.That(opened.Definition?.Pages, Has.Count.EqualTo(2), "it opens as it was stored");
            Assert.That(opened.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.pages") }));
            AssertCode(async () => await Service.RestoreVersionAsync(created.Key, current.Form.ConcurrencyVersion, 2, Editor), "limit_exceeded", "$.pages");
            Assert.That((await Service.GetDraftAsync(created.Key))!.DraftVersion, Is.EqualTo(3), "nothing restored");
        });
    }

    [Test]
    public async Task ADuplicate_OfADraftAboveALoweredLimit_IsRefusedWithLimitExceeded_WritingNothing()
    {
        var created = await TwoPagesAsync();
        var forms = Count(FormBuilderTables.Form);

        Assert.Multiple(() =>
        {
            AssertCode(async () => await Service.DuplicateAsync(created.Key, new DuplicateFormRequest("Copy", IncludeActions: true, UserKey: Editor)), "limit_exceeded", "$.pages");
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(forms));
        });
    }

    [Test]
    public async Task CopiedActions_ComeFromASourceAboveALoweredLimit_ButNotIntoATargetAboveIt()
    {
        var source = await TwoPagesAsync(new FormAction { Key = Guid.NewGuid(), Type = "webhook", Name = "CRM", Enabled = false });
        var within = await Service.CreateAsync(new NewFormRequest("Within", UserKey: Editor));
        var above = await TwoPagesAsync();

        var copied = (await Service.CopyActionsAsync(new CopyActionsRequest(source.Key, within.Key, within.ConcurrencyVersion, UserKey: Editor)))!;

        Assert.Multiple(async () =>
        {
            Assert.That(copied.Actions, Has.Count.EqualTo(1), "a source above the limit still gives its actions");
            Assert.That((await Service.GetDraftAsync(within.Key))!.Definition!.Actions.Select(action => action.Name), Is.EqualTo(new[] { "CRM" }));
            AssertCode(async () => await Service.CopyActionsAsync(new CopyActionsRequest(source.Key, above.Key, above.ConcurrencyVersion, UserKey: Editor)), "limit_exceeded", "$.pages");
            Assert.That((await Service.GetDraftAsync(above.Key))!.Definition!.Actions, Is.Empty, "the target stays as it was");
        });
    }

    [Test]
    public async Task ASavedTemplateAboveALoweredLimit_IsRefusedWithLimitExceeded()
    {
        var page = new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] };
        var definition = new FormDefinition { Key = Guid.NewGuid(), Pages = [page, page with { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }], Outcomes = [new FormOutcome { Key = Guid.NewGuid() }] };
        var template = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Two pages", FormDefinitionSerializer.WriteString(definition), UserKey: Editor));

        AssertCode(async () => await Service.CreateFromTemplateAsync(new TemplateFormRequest(template.Key, UserKey: Editor)), "limit_exceeded", "$.pages");
        Assert.That((await Service.CreateFromTemplateAsync(new TemplateFormRequest(BuiltInFormTemplates.ContactKey, UserKey: Editor)))?.Alias, Is.EqualTo("contact"), "a built-in template has one page");
    }

    [Test]
    public async Task AnImportAboveTheSitesLimit_IsRefused_AtItsPlaceInTheFile()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var envelope = JsonNode.Parse((await Service.ExportAsync(created.Key))!.Json)!.AsObject();
        envelope["definition"] = JsonNode.Parse(FormDefinitionSerializer.WriteString(blank with { Pages = [blank.Pages[0], new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }] }));
        var forms = Count(FormBuilderTables.Form);

        Assert.Multiple(() =>
        {
            AssertCode(async () => await Service.ImportAsync(new ImportFormRequest(envelope.ToJsonString(), UserKey: Editor)), "limit_exceeded", "$.definition.pages");
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(forms));
        });
    }

    // A form whose draft has two pages, stored as it was while the host allowed more, with the actions given.
    private async Task<FormInfo> TwoPagesAsync(params FormAction[] actions)
    {
        var created = await Service.CreateAsync(new NewFormRequest("Above", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var twoPages = blank with { Pages = [blank.Pages[0], new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }], Actions = actions };
        var row = (await Forms.GetAsync(created.Key))!;
        await Forms.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Stored(twoPages), null, Editor);
        return (await Service.GetAsync(created.Key))!;
    }
}
