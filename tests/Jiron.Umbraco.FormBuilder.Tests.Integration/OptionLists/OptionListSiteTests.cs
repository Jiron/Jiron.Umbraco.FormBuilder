using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;

/// <summary>
/// Option lists in a real site (ADR-0073): publishing holds a field's source configuration to its kind at its path, a
/// library list read through the database shows its changes at once and tells every server, and the site resolves the
/// standard lists through its registrations.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class OptionListSiteTests : RepositoryTestBase
{
    private static readonly Guid ListKey = Guid.Parse("74000000-0000-4000-8000-000000000001");

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private IFormBuilderOptionService Options => GetRequiredService<IFormBuilderOptionService>();

    private OptionListService Lists => GetRequiredService<OptionListService>();

    private RecordingServerMessenger Messenger => GetRequiredService<RecordingServerMessenger>();

    [Test]
    public async Task Publishing_RefusesAFieldsSourceConfigurationThatItsKindRefuses_AtItsPath_AndPublishesItOnceFixed()
    {
        var form = await Service.CreateAsync(new NewFormRequest("Booking", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(form.Key))!.Definition!;
        var saved = await Service.SaveDraftAsync(form.Key, form.ConcurrencyVersion, WithFields(blank, Choice("""{"source":"products"}"""), Country("""{"countries":["CH","XX"]}""")), null, Editor);

        var refusal = AssertCode(async () => await Service.PublishAsync(form.Key, saved!.Form.ConcurrencyVersion, null, Editor), "setting_invalid") as FormBuilderValidationException;
        var fixedDraft = await Service.SaveDraftAsync(form.Key, saved!.Form.ConcurrencyVersion, WithFields(blank, Choice("""{"source":"months"}"""), Country("""{"countries":["CH","LI"]}""")), null, Editor);
        var published = await Service.PublishAsync(form.Key, fixedDraft!.Form.ConcurrencyVersion, null, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(refusal?.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[]
            {
                (DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[0].options.config.source"),
                (DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[1].options.config.countries[1]"),
            }), "a draft keeps them; publishing refuses them");
            Assert.That(refusal?.Problems[0].Message, Is.EqualTo("The site has no option source 'products'."));
            Assert.That(published!.Version, Is.EqualTo(4), "once fixed it goes live, after the first draft and the two saves");
        });
    }

    [Test]
    public async Task AChangedList_ShowsAtOnce_AndEveryServerHearsOfEachChange()
    {
        var created = await Lists.CreateAsync(ListKey, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","label":{"":"Small","de":"Klein"}}]"""));
        var field = new FormField { Key = Guid.NewGuid(), Type = "dropdown", Alias = "size", Options = new FieldOptions { Source = OptionSource.List, OptionList = ListKey } };

        var before = await Options.GetAsync(field, new FormBuilderOptionContext("de-CH"));
        var changed = await Lists.UpdateAsync(ListKey, created.List!.ConcurrencyVersion, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"m","label":{"":"Medium"}}]"""));
        var after = await Options.GetAsync(field, new FormBuilderOptionContext("de-CH"));
        var deleted = await Lists.DeleteAsync(ListKey, changed.List!.ConcurrencyVersion);
        var gone = await Options.GetListAsync(ListKey, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(before.Options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("s", "Klein") }));
            Assert.That(after.Options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("m", "Medium") }), "the change shows at once");
            Assert.That(deleted, Is.True);
            Assert.That(gone.IsAvailable, Is.False, "a deleted list is gone at once");
            Assert.That(Messenger.Refreshes.Count(refresh => refresh.Refresher == FormBuilderOptionListCacheRefresher.UniqueId), Is.EqualTo(3), "the create, the change and the delete");
        });
    }

    [Test]
    public async Task TheSite_RegistersTheRefresherTheServicesAndTheStandardLists()
    {
        var months = new FormField { Key = Guid.NewGuid(), Type = "dropdown", Alias = "month", Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"months"}""") } };

        var german = await Options.GetAsync(months, new FormBuilderOptionContext("de"));

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<CacheRefresherCollection>()[FormBuilderOptionListCacheRefresher.UniqueId], Is.InstanceOf<FormBuilderOptionListCacheRefresher>());
            Assert.That(Options, Is.InstanceOf<FormBuilderOptionService>());
            Assert.That(GetRequiredService<FormBuilderOptionSourceCollection>().Select(source => source.GetType()), Is.EqualTo(StandardOptionSources.All));
            Assert.That(GetRequiredService<IEnumerable<IFormPublishCheck>>().Select(check => check.GetType()), Is.EqualTo(new[] { typeof(StructuralPublishCheck), typeof(OptionSourcePublishCheck) }), "the structural validation first");
            Assert.That(german.Options.Select(option => option.Label).Take(2), Is.EqualTo(new[] { "Januar", "Februar" }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
        builder.Services.AddSingleton<RecordingServerMessenger>();
        builder.Services.AddUnique<IServerMessenger>(provider => provider.GetRequiredService<RecordingServerMessenger>());
    }

    private static FormField Choice(string config) => new()
    {
        Key = Guid.Parse("74000000-0000-4000-8000-000000000010"),
        Type = "dropdown",
        Alias = "product",
        Label = new LocalizedText("Product"),
        Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse(config) },
    };

    private static FormField Country(string config) => new()
    {
        Key = Guid.Parse("74000000-0000-4000-8000-000000000011"),
        Type = "dropdown",
        Alias = "country",
        Label = new LocalizedText("Country"),
        Options = new FieldOptions { Source = OptionSource.Countries, Config = SettingsObject.Parse(config) },
    };
}
