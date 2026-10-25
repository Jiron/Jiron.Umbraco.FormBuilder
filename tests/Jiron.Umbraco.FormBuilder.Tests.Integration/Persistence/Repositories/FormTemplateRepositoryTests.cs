using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>The templates users saved (ADR-0037): whole forms and groups of fields, without a concurrency version.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormTemplateRepositoryTests : RepositoryTestBase
{
    private const string Definition = """{"schemaVersion":1,"pages":[]}""";

    [Test]
    public async Task ATemplate_IsSaved_AndReadByKeyAndId()
    {
        var key = Guid.NewGuid();

        var saved = await Templates.CreateAsync(new NewFormTemplate(key, FormTemplateKind.Group, "Address", Definition, "Street, town and country", "Blocks", Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(saved with { Id = 0 }, Is.EqualTo(new StoredFormTemplate(0, key, FormTemplateKind.Group, "Address", "Street, town and country", "Blocks", Definition, Now, Editor, Now, Editor)), "a new template was last changed when it was saved");
            Assert.That(await Templates.GetAsync(key), Is.EqualTo(saved));
            Assert.That(await Templates.GetByIdAsync(saved.Id), Is.EqualTo(saved));
            Assert.That(await Templates.GetAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task ATakenKey_IsRefused_AndNothingIsWritten()
    {
        var saved = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Contact", Definition));

        Assert.Multiple(() =>
        {
            AssertRefused(() => Templates.CreateAsync(new NewFormTemplate(saved.Key, FormTemplateKind.Group, "Other", Definition)), FormBuilderConflict.DuplicateKey);
            Assert.That(Count(FormBuilderTables.FormTemplate), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Lists_FilterByKindCategoryAndText_PageInTheDatabase_AndCountTheWhole()
    {
        var contact = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Contact", Definition, "Asks 100% of what we need", "General"));
        var survey = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Survey", Definition, "Up to 1000 answers", "Feedback"));
        var address = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Group, "Address", Definition, Category: "general"));
        var consent = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Group, "Consent_block", Definition));

        Assert.Multiple(async () =>
        {
            var all = await Templates.ListAsync(new FormTemplateFilter(), PageRequest.First(500));
            Assert.That(all.Items.Select(t => t.Id), Is.EqualTo(new[] { address.Id, consent.Id, contact.Id, survey.Id }), "in name order");
            Assert.That(all.Total, Is.EqualTo(4));
            Assert.That((await Templates.ListAsync(new FormTemplateFilter(Kind: FormTemplateKind.Group), PageRequest.First(10))).Items.Select(t => t.Id), Is.EqualTo(new[] { address.Id, consent.Id }));
            Assert.That((await Templates.ListAsync(new FormTemplateFilter(Category: "GENERAL"), PageRequest.First(10))).Items.Select(t => t.Id), Is.EqualTo(new[] { address.Id, contact.Id }), "a category in any letter case");
            Assert.That((await Templates.ListAsync(new FormTemplateFilter(FormTemplateKind.Form, "general"), PageRequest.First(10))).Items.Select(t => t.Id), Is.EqualTo(new[] { contact.Id }));
            Assert.That((await Templates.ListAsync(new FormTemplateFilter(Search: "100%"), PageRequest.First(10))).Items.Select(t => t.Id), Is.EqualTo(new[] { contact.Id }), "the description, with the percent sign no wildcard, or it would find the survey");
            Assert.That((await Templates.ListAsync(new FormTemplateFilter(Search: "N_A"), PageRequest.First(10))).Items, Is.Empty, "the underscore is no wildcard, or it would find Contact");
            Assert.That((await Templates.ListAsync(new FormTemplateFilter(Search: "nt_b"), PageRequest.First(10))).Items.Select(t => t.Id), Is.EqualTo(new[] { consent.Id }));
            var page = await Templates.ListAsync(new FormTemplateFilter(), new PageRequest(2, 1));
            Assert.That(page.Items.Select(t => t.Id), Is.EqualTo(new[] { contact.Id }));
            Assert.That(page.Total, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task AnUpdate_ReplacesTheTexts_AndTheLastOneWins_RecordingWhoChangedItAndWhen()
    {
        var saved = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Contact", Definition, "Old", "General", Editor));
        const string Newer = """{"schemaVersion":1,"pages":[{"key":"0f8fad5b-d9cb-469f-a165-70867728950e"}]}""";
        var reviser = Guid.NewGuid();

        Clock.Advance(TimeSpan.FromMinutes(5));
        await Templates.UpdateAsync(new FormTemplateUpdate(saved.Id, "Contact us", null, null, Newer, Editor));
        Clock.Advance(TimeSpan.FromMinutes(5));
        var last = await Templates.UpdateAsync(new FormTemplateUpdate(saved.Id, "Contact, second", "New", "Support", Newer, reviser));

        Assert.Multiple(async () =>
        {
            Assert.That(
                last,
                Is.EqualTo(saved with { Name = "Contact, second", Description = "New", Category = "Support", Definition = Newer, UpdatedUtc = Now.AddMinutes(10), UpdatedByUserKey = reviser }),
                "the kind and who saved it when stay; the change and who made it are the last ones");
            Assert.That(await Templates.GetByIdAsync(saved.Id), Is.EqualTo(last));
            Assert.That(await Templates.UpdateAsync(new FormTemplateUpdate(-1, "Missing", null, null, Definition, Editor)), Is.Null);
        });
    }

    [Test]
    public async Task ADelete_RemovesTheTemplate()
    {
        var saved = await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Contact", Definition));

        Assert.Multiple(async () =>
        {
            Assert.That(await Templates.DeleteAsync(saved.Id), Is.True);
            Assert.That(await Templates.DeleteAsync(saved.Id), Is.False, "deleted already");
            Assert.That(await Templates.GetAsync(saved.Key), Is.Null);
        });
    }
}
