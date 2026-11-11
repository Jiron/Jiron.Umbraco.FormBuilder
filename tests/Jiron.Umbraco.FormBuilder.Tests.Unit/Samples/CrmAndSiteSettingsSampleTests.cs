using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Samples.Crm;
using Jiron.Umbraco.FormBuilder.Samples.SiteSettings;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Samples;

/// <summary>
/// The CRM action and the site settings source of the samples (docs/extending.md, ADR-0234): the action writes the
/// contact with merge tags, adds it once per delivery, tries again while the CRM is away and fails without an address,
/// and its test adds nothing; the source reads the settings node of the form's site, else the one at the root, in the
/// culture of the template.
/// </summary>
public sealed class CrmAndSiteSettingsSampleTests
{
    // ---- The CRM action ----------------------------------------------------------------------------------------------

    [Test]
    public async Task TheAction_AddsTheContactItsTagsWrite_OncePerDelivery()
    {
        var crm = new StubCrmClient(NullLogger<StubCrmClient>.Instance);
        var action = new CrmActionType(Tags(("{{ field.email }}", " ada@visitor.jfb.test "), ("{{ field.name }}", "Ada")), crm);
        var run = Run("""{"email":"{{ field.email }}","name":"{{ field.name }}","list":"leads"}""", "delivery-1");

        var first = await action.RunAsync(run, CancellationToken.None);
        var again = await action.RunAsync(run with { Attempt = 2 }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((first, again), Is.EqualTo((ActionResult.Succeeded, ActionResult.Succeeded)));
            Assert.That(crm.Contacts, Has.Count.EqualTo(1), "one delivery, one contact");
            Assert.That(crm.Contacts["delivery-1"], Is.EqualTo(new CrmContact("ada@visitor.jfb.test", "Ada", CrmList.Leads)));
        });
    }

    [Test]
    public async Task ACrmThatIsAway_IsTriedAgain_AndAnEntryWithoutAnAddress_Fails()
    {
        var crm = new StubCrmClient(NullLogger<StubCrmClient>.Instance);
        var away = await new CrmActionType(Tags(("{{ field.email }}", "ada@unavailable.test")), crm).RunAsync(Run("""{"email":"{{ field.email }}"}""", "d-2"), CancellationToken.None);
        var none = await new CrmActionType(Tags(("{{ field.email }}", "  ")), crm).RunAsync(Run("""{"email":"{{ field.email }}"}""", "d-3"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((away.Kind, away.Code), Is.EqualTo((ActionResultKind.TryAgain, (string?)"crm_unavailable")));
            Assert.That((none.Kind, none.Code), Is.EqualTo((ActionResultKind.Failed, (string?)"no_email")));
            Assert.That(crm.Contacts, Is.Empty);
        });
    }

    [Test]
    public async Task ATest_ShowsTheContact_AndAddsNothing()
    {
        var crm = Substitute.For<ICrmClient>();
        var action = new CrmActionType(Tags(("{{ field.email }}", "ada@visitor.jfb.test"), (string.Empty, string.Empty)), crm);
        var test = new ActionTestContext
        {
            Action = Action("""{"email":"{{ field.email }}"}"""),
            Scope = Scope(),
            User = new ActionTestUser(Guid.NewGuid(), "editor@site.test", "Editor"),
        };

        var result = await action.TestAsync(test, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((result.Outcome, result.Code), Is.EqualTo((ActionTestOutcome.DryRun, (string?)ActionTestCodes.DryRun)));
            Assert.That(result.Preview?.Select(line => line.Text), Is.EqualTo(new[] { " <ada@visitor.jfb.test> to the list Newsletter" }));
            Assert.That(crm.ReceivedCalls(), Is.Empty);
            Assert.That(action.SettingsSchema.Settings.Select(setting => (setting.Name, setting.SupportsMergeTags)), Is.EqualTo(new[] { ("email", true), ("name", true), ("list", false) }));
        });
    }

    // ---- The site settings source ------------------------------------------------------------------------------------

    [Test]
    public async Task ATag_ReadsTheSettingsOfTheFormsSite_ElseTheOneAtTheRoot_InTheTemplatesCulture()
    {
        var site = Guid.NewGuid();
        var ownSettings = Settings(("phone", "+41 44 000 00 00", false), ("hours", "Mo-Fr 9-18 Uhr", true));
        var rootSettings = Settings(("phone", "+1 555 0100", false));
        var contents = Substitute.For<IPublishedContentCache>();
        contents.GetByIdAsync(ownSettings.Key, Arg.Any<bool?>()).Returns(ownSettings);
        contents.GetByIdAsync(rootSettings.Key, Arg.Any<bool?>()).Returns(rootSettings);
        var navigation = Substitute.For<IDocumentNavigationQueryService>();
        navigation.TryGetDescendantsKeysOfType(site, "siteSettings", out Arg.Any<IEnumerable<Guid>>())
            .Returns(call => { call[2] = new[] { ownSettings.Key }; return true; });
        navigation.TryGetRootKeysOfType("siteSettings", out Arg.Any<IEnumerable<Guid>>())
            .Returns(call => { call[1] = new[] { rootSettings.Key }; return true; });
        var source = new SiteSettingsMergeTagSource(contents, navigation, Substitute.For<IContentTypeService>());

        async Task<string?> Resolve(string path, Guid? from) =>
            (await source.ResolveAsync(new MergeTagSourceRequest(path, Scope(from, "de-CH"), MergeTagPlace.Mail, MergeTagContext.Text), CancellationToken.None))?.Items.Single().Text;

        Assert.Multiple(async () =>
        {
            Assert.That(await Resolve("phone", site), Is.EqualTo("+41 44 000 00 00"));
            Assert.That(await Resolve("hours", site), Is.EqualTo("Mo-Fr 9-18 Uhr (de-CH)"), "a property that varies, in the template's culture");
            Assert.That(await Resolve("phone", null), Is.EqualTo("+1 555 0100"), "no site: the one at the root");
            Assert.That(await source.ResolveAsync(new MergeTagSourceRequest("fax", Scope(site), MergeTagPlace.Mail, MergeTagContext.Text), CancellationToken.None), Is.Null, "an unknown path");
            Assert.That((source.Name, source.IsPerVisitor), Is.EqualTo(("siteSettings", false)));
        });
    }

    private static IFormBuilderMergeTagService Tags(params (string Template, string Text)[] rendered)
    {
        var tags = Substitute.For<IFormBuilderMergeTagService>();
        tags.RenderAsync(Arg.Any<string>(), MergeTagPlace.Action, MergeTagContext.Text, Arg.Any<MergeTagScope>(), Arg.Any<CancellationToken>())
            .Returns(call => new MergeTagRendering(rendered.FirstOrDefault(each => each.Template == call.ArgAt<string>(0)).Text ?? string.Empty, []));
        return tags;
    }

    private static FormAction Action(string settings) =>
        new() { Key = Guid.NewGuid(), Type = CrmActionType.TypeAlias, Settings = SettingsObject.Parse(settings) };

    private static ActionRunContext Run(string settings, string deliveryKey) => new()
    {
        Action = Action(settings),
        Trigger = ActionTrigger.Submitted,
        Scope = Scope(),
        Attempt = 1,
        DeliveryKey = deliveryKey,
    };

    private static MergeTagScope Scope(Guid? site = null, string culture = "en-US") => new()
    {
        Form = new FormDefinition { Key = Guid.NewGuid() },
        FormName = "Newsletter",
        FormAlias = "newsletter",
        Culture = culture,
        Site = site,
    };

    // A settings node whose properties give their value, one that varies by culture with the culture it was asked in.
    private static IPublishedContent Settings(params (string Alias, string Value, bool Varies)[] properties)
    {
        var content = Substitute.For<IPublishedContent>();
        content.Key.Returns(Guid.NewGuid());
        content.GetProperty(Arg.Any<string>()).Returns((IPublishedProperty?)null);
        foreach (var (alias, value, varies) in properties)
        {
            var type = Substitute.For<IPublishedPropertyType>();
            type.Variations.Returns(varies ? ContentVariation.Culture : ContentVariation.Nothing);
            var property = Substitute.For<IPublishedProperty>();
            property.PropertyType.Returns(type);
            property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(call => call.ArgAt<string?>(0) is { } culture ? $"{value} ({culture})" : value);
            content.GetProperty(alias).Returns(property);
        }

        return content;
    }
}
