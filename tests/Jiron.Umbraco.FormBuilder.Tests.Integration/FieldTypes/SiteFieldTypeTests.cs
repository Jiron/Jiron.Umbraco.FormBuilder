using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.FieldTypes;

/// <summary>
/// A real site whose composers change the field types (ADR-0055): one that runs before the core composer adds a type,
/// one that runs after it removes a built-in type. The registry, start-up, publishing, the field catalogue and the
/// public projection all see the site's types.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SiteFieldTypeTests : RepositoryTestBase
{
    private static readonly FormField Colour = new()
    {
        Key = Guid.Parse("71000000-0000-4000-8000-000000000001"),
        Type = "acme.colour",
        Alias = "colour",
        Label = new LocalizedText("Colour"),
        Settings = SettingsObject.Parse("""{"source":"brand","swatches":["red","blue"]}"""),
    };

    private static readonly FormField Signature = new()
    {
        Key = Guid.Parse("71000000-0000-4000-8000-000000000002"),
        Type = "signature",
        Alias = "signature",
        Label = new LocalizedText("Signature"),
    };

    /// <summary>Every test's site turns the composers on.</summary>
    public SiteFieldTypeTests() => InMemoryConfiguration[SiteFieldTypes.Switch] = "true";

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private FieldTypeRegistry Registry => GetRequiredService<FieldTypeRegistry>();

    [Test]
    public void TheSitesComposers_ChangeTheRegistry_BeforeAndAfterTheCoreComposer_AndTheSiteStarts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Registry.FindType("acme.colour"), Is.InstanceOf<ColourFieldType>(), "added before the core composer");
            Assert.That(Registry.Find("signature"), Is.Null, "removed after it");
            Assert.That(Registry.Types[^1], Is.InstanceOf<ColourFieldType>(), "after the built-in types");
            Assert.That(Registry.Types, Has.Length.EqualTo(BuiltInFieldTypes.All.Length));
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing);
        });
    }

    [Test]
    public async Task AFieldOfTheSitesType_Publishes_IntoTheCatalogue_AndVisitorsSeeItsPublicSettingsOnly()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Colours", UserKey: Editor));
        var draft = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var definition = WithFields(draft, Colour);
        var saved = (await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, definition, null, Editor))!;
        var published = (await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor))!;
        var row = (await Forms.GetAsync(created.Key))!;
        var version = (await Forms.GetVersionAsync(row.Id, published.Version!.Value))!;
        var field = (await Forms.ListFieldsAsync(row.Id)).Single().Values;
        var projection = PublicProjection.Create(definition, Registry);

        Assert.Multiple(() =>
        {
            Assert.That(published.Version, Is.EqualTo(saved.Version + 1), "the draft went live");
            Assert.That((field.Alias, field.Type, field.StorageKind), Is.EqualTo(("colour", "acme.colour", FieldStorageKind.Text)));
            Assert.That(projection.Pages[0].Groups[0].Fields[0].Settings?.ToString(), Is.EqualTo("""{"swatches":["red","blue"]}"""));
            Assert.That(version.Info.PublicHash, Is.EqualTo(PublicProjection.Hash(projection)), "the site's registry made the public revision");
        });
    }

    [Test]
    public async Task AFieldOfTheRemovedType_IsUnknown_AndTheFormDoesNotPublish()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Signed", UserKey: Editor));
        var draft = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var saved = (await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, WithFields(draft, Signature), null, Editor))!;

        AssertCode(async () => await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor), DefinitionErrors.UnknownType, "$.pages[0].groups[0].fields[0].type");
        Assert.That(Count(FormBuilderTables.FormField), Is.Zero, "nothing reached the catalogue");
    }
}
