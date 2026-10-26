using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// A site whose host locks two settings of the text type (ADR-0060): the maximum length to 100, shown read-only, and
/// the prefix hidden, keeping its default. A form saved before the locks came keeps its own values in store, runs with
/// the locked ones, publishes after the checks of what it runs, and saves only while a locked setting keeps the lock's
/// value or the one it held.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FieldSettingLockServiceTests : RepositoryTestBase
{
    private const string Locks = FormBuilderOptions.SectionName + ":Designer:SettingLocks:";

    private static readonly FormField Name = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000001"), Type = "text", Alias = "name", Label = new LocalizedText("Name") };

    /// <summary>The host configuration of every test's site, as a host passes it in its appsettings.json.</summary>
    public FieldSettingLockServiceTests()
    {
        InMemoryConfiguration[Locks + "0:Kind"] = "field";
        InMemoryConfiguration[Locks + "0:Type"] = "text";
        InMemoryConfiguration[Locks + "0:Setting"] = "maxLength";
        InMemoryConfiguration[Locks + "0:Value"] = "100";
        InMemoryConfiguration[Locks + "1:Kind"] = "Field";
        InMemoryConfiguration[Locks + "1:Type"] = "text";
        InMemoryConfiguration[Locks + "1:Setting"] = "prefix";
        InMemoryConfiguration[Locks + "1:Hidden"] = "true";
    }

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    [Test]
    public async Task AFormSavedBeforeTheLocks_RunsWithTheLockedValues_AndKeepsItsOwnInStore()
    {
        var created = await BeforeTheLocksAsync("""{"counter":true,"maxLength":500,"prefix":{"":"A"}}""");
        var draft = (await Service.GetDraftAsync(created))!;

        var published = (await Service.PublishAsync(created, draft.Form.ConcurrencyVersion, null, Editor))!;
        var running = (await Service.GetPublishedAsync(created))!;
        var field = running.Definition.Pages[0].Groups[0].Fields.Single();
        var stored = await Forms.GetVersionByIdAsync((await Forms.GetAsync(created))!.PublishedVersionId!.Value);
        var types = GetRequiredService<FieldTypeRegistry>();

        Assert.Multiple(() =>
        {
            Assert.That(field.Settings?.ToString(), Is.EqualTo("""{"counter":true,"maxLength":100}"""), "the locked value, and no prefix: it keeps its default");
            Assert.That(((TextFieldType)types.FindType("text")!).ReadSettings(field.Settings).MaxLength, Is.EqualTo(100), "the type reads the locked value");
            Assert.That(PublicProjection.Create(running.Definition, types).Pages[0].Groups[0].Fields[0].Settings?.ToString(), Is.EqualTo("""{"counter":true,"maxLength":100,"minLength":0}"""), "visitors get it, with the defaults of the other public settings (ADR-0062)");
            Assert.That((running.Version, running.Form.Key), Is.EqualTo((published.Version!.Value, created)));
            Assert.That(stored!.Definition, Does.Contain("\"maxLength\":500").And.Contain("\"prefix\":{\"\":\"A\"}"), "nothing was rewritten");
            Assert.That(draft.Definition!.Pages[0].Groups[0].Fields[0].Settings?.ToString(), Is.EqualTo("""{"counter":true,"maxLength":500,"prefix":{"":"A"}}"""), "the designer reads what the editor stored");
        });
    }

    [Test]
    public async Task ADraftSave_RefusesAnotherValueOfALockedSetting_AndKeepsTheOneTheDraftHeld()
    {
        var created = await BeforeTheLocksAsync("""{"maxLength":500,"prefix":{"":"A"}}""");
        var draft = (await Service.GetDraftAsync(created))!;
        var version = draft.Form.ConcurrencyVersion;
        FormDefinition With(string settings, string label = "Name") =>
            WithFields(draft.Definition!, Name with { Label = new LocalizedText(label), Settings = SettingsObject.Parse(settings) });

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.SaveDraftAsync(created, version, With("""{"maxLength":400,"prefix":{"":"A"}}"""), null, Editor),
                Throws.InstanceOf<FormBuilderSettingLockedException>()
                    .With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("$.pages[0].groups[0].fields[0].settings.maxLength")
                    .And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(Locks + "0"));
            Assert.That(
                async () => await Service.SaveDraftAsync(created, version, With("""{"maxLength":500,"prefix":{"":"B"}}"""), null, Editor),
                Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(Locks + "1"),
                "a hidden setting too");
            Assert.That(
                async () => await Service.SaveDraftAsync(created, version, WithFields(draft.Definition!, Name with { Key = Guid.NewGuid(), Settings = SettingsObject.Parse("""{"maxLength":500}""") }), null, Editor),
                Throws.InstanceOf<FormBuilderSettingLockedException>(),
                "a new field takes the lock's value or none");
        });
        Assert.That((await Service.GetDraftAsync(created))!.DraftVersion, Is.EqualTo(draft.DraftVersion), "nothing was written");

        var relabelled = (await Service.SaveDraftAsync(created, version, With("""{"maxLength":500,"prefix":{"":"A"}}""", "Full name"), null, Editor))!;
        var locked = (await Service.SaveDraftAsync(created, relabelled.Form.ConcurrencyVersion, With("""{"maxLength":100}"""), null, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That(relabelled.Version, Is.EqualTo(draft.DraftVersion + 1), "the values it held, beside another change");
            Assert.That(locked.Version, Is.EqualTo(draft.DraftVersion + 2), "the lock's value, and the hidden setting left out");
        });
    }

    [Test]
    public async Task APublish_ChecksTheDraftAsTheSiteWillRunIt()
    {
        var created = await BeforeTheLocksAsync("""{"maxLength":500,"minLength":150}""");
        var draft = (await Service.GetDraftAsync(created))!;

        AssertCode(async () => await Service.PublishAsync(created, draft.Form.ConcurrencyVersion, null, Editor), DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[0].settings.minLength");

        var fixedDraft = (await Service.SaveDraftAsync(created, draft.Form.ConcurrencyVersion, WithFields(draft.Definition!, Name with { Settings = SettingsObject.Parse("""{"maxLength":500,"minLength":50}""") }), null, Editor))!;
        var published = await Service.PublishAsync(created, fixedDraft.Form.ConcurrencyVersion, null, Editor);

        Assert.That(published?.Version, Is.EqualTo(fixedDraft.Version + 1), "within the locked maximum, it goes live as a version of its own");
    }

    [Test]
    public async Task ThePublishedForm_IsNoneForAMissingAnUnpublishedOrATrashedForm()
    {
        var created = await BeforeTheLocksAsync("""{"maxLength":100}""");
        var unpublished = await Service.GetPublishedAsync(created);
        var draft = (await Service.GetDraftAsync(created))!;
        var published = (await Service.PublishAsync(created, draft.Form.ConcurrencyVersion, null, Editor))!;
        var live = await Service.GetPublishedAsync(created);
        await Service.TrashAsync(created, published.Form.ConcurrencyVersion, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(await Service.GetPublishedAsync(Guid.NewGuid()), Is.Null, "missing");
            Assert.That(unpublished, Is.Null, "not published");
            Assert.That(live?.Definition.Pages[0].Groups[0].Fields[0].Settings?.ToString(), Is.EqualTo("""{"maxLength":100}"""));
            Assert.That(await Service.GetPublishedAsync(created), Is.Null, "in the trash");
        });
    }

    [Test]
    public void TheSite_ResolvesItsLocks_AndChecksThemWhenItStarts()
    {
        var locks = GetRequiredService<FieldSettingLocks>().Of("text");

        Assert.Multiple(() =>
        {
            Assert.That(locks.Select(each => (each.Setting.Name, each.ConfigurationKey, each.Value?.GetRawText(), each.Hidden)), Is.EqualTo(new[] { ("maxLength", Locks + "0", (string?)"100", false), ("prefix", Locks + "1", null, true) }));
            Assert.That(GetRequiredService<IEnumerable<IValidateOptions<FormBuilderStartupChecks>>>().OfType<FieldSettingLockStartupCheck>(), Has.Exactly(1).Items);
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing);
        });
    }

    // A form whose draft holds a text field with these settings, as it was saved before the host locked them.
    private async Task<Guid> BeforeTheLocksAsync(string settings)
    {
        var created = await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var row = (await Forms.GetAsync(created.Key))!;
        await Forms.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Stored(WithFields(blank, Name with { Settings = SettingsObject.Parse(settings) })), null, Editor);
        return created.Key;
    }
}
