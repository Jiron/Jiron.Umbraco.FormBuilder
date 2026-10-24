using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// A form saved before the host lowered a definition limit (ADR-0042, ADR-0044): it still opens, with the problem a save
/// would have, and a save or a publish of it is refused with <c>limit_exceeded</c> until the editor brings it within.
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
}
