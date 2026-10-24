using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories.FormRepositoryTests;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The field catalogue (ADR-0003): a field's row is added once, kept current, and removed only while no entry value
/// refers to it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FieldCatalogueTests : RepositoryTestBase
{
    [Test]
    public async Task Fields_AreAdded_AndListedInColumnOrder()
    {
        var form = await CreateFormAsync();
        var parent = Guid.NewGuid();
        var email = Field("email", sortOrder: 2) with { IsSensitive = true, FirstVersionNumber = 2 };
        var name = Field("name", sortOrder: 1) with { ParentFieldKey = parent, LastVersionNumber = 4 };

        var added = await Forms.AddFieldAsync(form.Id, email);
        await Forms.AddFieldAsync(form.Id, name);
        var other = await CreateFormAsync();
        await Forms.AddFieldAsync(other.Id, Field("elsewhere"));

        var listed = await Forms.ListFieldsAsync(form.Id);
        Assert.Multiple(() =>
        {
            Assert.That(added?.FormId, Is.EqualTo(form.Id));
            Assert.That(added?.Values, Is.EqualTo(email), "every column round-trips");
            Assert.That(listed.Select(field => field.Values), Is.EqualTo(new[] { name, email }), "by sort order, this form only");
        });
    }

    [Test]
    public async Task AFieldIsAddedOnce_AndNotToAMissingForm()
    {
        var form = await CreateFormAsync();
        var field = Field("email");
        await Forms.AddFieldAsync(form.Id, field);

        Assert.Multiple(async () =>
        {
            AssertConflict(() => Forms.AddFieldAsync(form.Id, field with { Alias = "again" }), FormBuilderConflict.DuplicateKey);
            Assert.That(await Forms.AddFieldAsync(-1, Field("orphan")), Is.Null);
            Assert.That(Count(FormBuilderTables.FormField), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AField_IsUpdatedByItsKey()
    {
        var form = await CreateFormAsync();
        var field = Field("email");
        await Forms.AddFieldAsync(form.Id, field);
        var changed = field with { Alias = "workEmail", Label = "Work email", SortOrder = 9, IsSensitive = true, FirstVersionNumber = 2, LastVersionNumber = 3 };

        var updated = await Forms.UpdateFieldAsync(form.Id, changed);

        Assert.Multiple(async () =>
        {
            Assert.That(updated?.Values, Is.EqualTo(changed));
            Assert.That(await Forms.UpdateFieldAsync(form.Id, Field("missing")), Is.Null);
        });
    }

    [Test]
    public async Task AField_IsRemoved_OnlyWhileNoValueRefersToIt()
    {
        var form = await CreateFormAsync();
        var free = (await Forms.AddFieldAsync(form.Id, Field("free")))!;
        var answered = (await Forms.AddFieldAsync(form.Id, Field("answered")))!;
        await AddValueAsync(await AddEntryAsync(form.Id, form.DraftVersionId!.Value), answered.Id);

        Assert.Multiple(async () =>
        {
            AssertConflict(() => Forms.DeleteFieldAsync(form.Id, answered.Values.FieldKey), FormBuilderConflict.InUse);
            Assert.That(await Forms.DeleteFieldAsync(form.Id, free.Values.FieldKey), Is.True);
            Assert.That(await Forms.DeleteFieldAsync(form.Id, free.Values.FieldKey), Is.False, "removed already");
            Assert.That((await Forms.ListFieldsAsync(form.Id)).Select(field => field.Id), Is.EqualTo(new[] { answered.Id }));
        });
    }
}
