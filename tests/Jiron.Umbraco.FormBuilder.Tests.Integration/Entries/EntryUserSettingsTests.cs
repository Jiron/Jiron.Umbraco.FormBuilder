using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>A user's columns and filter sets (ROADMAP 11.1, ADR-0207, ADR-0008): kept in jfbUserSetting, the same on every device.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryUserSettingsTests : ActionTestBase
{
    private static readonly Guid Someone = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c086");
    private static readonly Guid Other = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c087");
    private static readonly Guid Form = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c088");

    private EntryUserSettings Settings => GetRequiredService<EntryUserSettings>();

    [Test]
    public async Task Columns_AreKeptPerUserAndForm_InTheirOrder_AndNoneRemovesThem()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var before = await Settings.GetColumnsAsync(Someone, Form);
        await Settings.SetColumnsAsync(Someone, Form, [second, first]);
        var kept = await Settings.GetColumnsAsync(Someone, Form);
        var others = await Settings.GetColumnsAsync(Other, Form);
        await Settings.SetColumnsAsync(Someone, Form, []);

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.Empty, "none until the user chooses");
            Assert.That(kept, Is.EqualTo(new[] { second, first }));
            Assert.That(others, Is.Empty, "a user's own");
            Assert.That(await GetRequiredService<IUserSettingRepository>().GetAsync(Someone, EntryUserSettings.ColumnsName(Form)), Is.Null, "no columns, no setting");
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Settings.SetColumnsAsync(Someone, Form, [.. Enumerable.Range(0, EntryUserSettings.MaxColumns + 1).Select(_ => Guid.NewGuid())]))!.Problems[0].Code, Is.EqualTo("too_many_columns"));
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Settings.SetColumnsAsync(Someone, Form, [first, first]))!.Problems[0].Code, Is.EqualTo("column_twice"));
        });
    }

    [Test]
    public async Task FilterSets_KeepTheirPlace_AndANameOncePerForm()
    {
        var waiting = await Settings.SaveFilterSetAsync(Someone, Set("Waiting", Form));
        var failed = await Settings.SaveFilterSetAsync(Someone, Set("Failed", Form));
        await Settings.SaveFilterSetAsync(Someone, waiting with { Name = "Still waiting", Filter = new EntryListFilter { Search = "ada" } });
        var everywhere = await Settings.SaveFilterSetAsync(Someone, Set("waiting", null));
        var sets = await Settings.GetFilterSetsAsync(Someone);

        Assert.Multiple(async () =>
        {
            Assert.That(sets.Select(set => set.Name), Is.EqualTo(new[] { "Still waiting", "Failed", "waiting" }), "a saved set keeps its place");
            Assert.That(sets[0].Filter.Search, Is.EqualTo("ada"));
            Assert.That(everywhere.Form, Is.Null, "the same name for every form is another set's");
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Settings.SaveFilterSetAsync(Someone, Set(" FAILED ", Form)))!.Problems[0].Code, Is.EqualTo("name_taken"));
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Settings.SaveFilterSetAsync(Someone, Set(new string('x', EntryUserSettings.MaxSetNameLength + 1), Form)))!.Problems[0].Code, Is.EqualTo("invalid_name"));
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Settings.SaveFilterSetAsync(Someone, Set("   ", Form)))!.Problems[0].Code, Is.EqualTo("invalid_name"));
            Assert.That(await Settings.GetFilterSetsAsync(Other), Is.Empty, "a user's own");
            Assert.That(await Settings.DeleteFilterSetAsync(Someone, failed.Key), Is.True);
            Assert.That(await Settings.DeleteFilterSetAsync(Someone, failed.Key), Is.False);
        });
    }

    [Test]
    public async Task FilterSets_StopAtFifty()
    {
        for (var i = 0; i < EntryUserSettings.MaxFilterSets; i++)
        {
            await Settings.SaveFilterSetAsync(Someone, Set($"Set {i}", Form));
        }

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => Settings.SaveFilterSetAsync(Someone, Set("One more", Form)));

        Assert.That(refused!.Problems[0].Code, Is.EqualTo("too_many_filter_sets"));
    }

    [Test]
    public async Task AStoredValueThatCannotBeRead_CountsAsNone_AndTheNextSaveReplacesIt()
    {
        var store = GetRequiredService<IUserSettingRepository>();
        await store.SetAsync(Someone, EntryUserSettings.FilterSetsName, """[{"key":"not a key"}]""");
        await store.SetAsync(Someone, EntryUserSettings.ColumnsName(Form), """{"broken":true}""");

        var sets = await Settings.GetFilterSetsAsync(Someone);
        var columns = await Settings.GetColumnsAsync(Someone, Form);
        await Settings.SaveFilterSetAsync(Someone, Set("Fresh", Form));

        Assert.Multiple(async () =>
        {
            Assert.That(sets, Is.Empty);
            Assert.That(columns, Is.Empty);
            Assert.That((await Settings.GetFilterSetsAsync(Someone)).Select(set => set.Name), Is.EqualTo(new[] { "Fresh" }));
        });
    }

    [Test]
    public async Task TheRepository_KeepsOneValuePerUserAndName()
    {
        var store = GetRequiredService<IUserSettingRepository>();

        await store.SetAsync(Someone, "entries.test", "1");
        await store.SetAsync(Someone, "entries.test", "2");
        await store.SetAsync(Other, "entries.test", "3");

        Assert.Multiple(async () =>
        {
            Assert.That(await store.GetAsync(Someone, "entries.test"), Is.EqualTo("2"));
            Assert.That(await store.GetAsync(Other, "entries.test"), Is.EqualTo("3"));
            Assert.That(await store.DeleteAsync(Someone, "entries.test"), Is.True);
            Assert.That(await store.GetAsync(Someone, "entries.test"), Is.Null);
            Assert.That(() => store.GetAsync(Guid.Empty, "entries.test"), Throws.ArgumentException);
            Assert.That(() => store.GetAsync(Someone, new string('n', UserSettingRepository.MaxNameLength + 1)), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    private static EntryFilterSet Set(string name, Guid? form) => new(Guid.NewGuid(), name, form, new EntryListFilter());
}
