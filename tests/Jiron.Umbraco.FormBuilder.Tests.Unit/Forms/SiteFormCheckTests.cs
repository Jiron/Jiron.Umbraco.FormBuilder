using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The form check's checks of what the site holds (ROADMAP 9.5e, ADR-0180): a theme the site lacks, the content, media
/// and member groups a form names that the site lacks, a redirect to a page that is not published, and a text that names
/// a dictionary item without a text in a language its visitors read; and every localised text of a form, with its path.
/// </summary>
[TestFixture]
public sealed class SiteFormCheckTests
{
    private const string Fields = "$.pages[0].groups[0].fields";

    private static readonly Guid Published = Guid.Parse("80000000-0000-4000-8000-000000000001");
    private static readonly Guid Unpublished = Guid.Parse("80000000-0000-4000-8000-000000000002");
    private static readonly Guid GonePage = Guid.Parse("80000000-0000-4000-8000-000000000003");
    private static readonly Guid Picture = Guid.Parse("81000000-0000-4000-8000-000000000001");
    private static readonly Guid GoneMedia = Guid.Parse("81000000-0000-4000-8000-000000000002");
    private static readonly Guid Gold = Guid.Parse("82000000-0000-4000-8000-000000000001");
    private static readonly Guid GoneGroup = Guid.Parse("82000000-0000-4000-8000-000000000002");

    private static readonly FieldTypeRegistry Types = TestFieldTypes.Registry;

    [Test]
    public async Task ATheme_TheSiteLacks_InCodeAndInTheLibrary_IsAWarning()
    {
        var library = Substitute.For<IThemeRepository>();
        library.GetByAliasAsync("brand", Arg.Any<CancellationToken>()).Returns(Task.FromResult<StoredTheme?>(new StoredTheme(1, Guid.NewGuid(), "brand", "Brand", "default", "{}", null, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)));
        var check = new ThemeFormCheck(new FormThemes(new FormBuilderThemes([new DefaultTheme(), new UnstyledTheme(), new Bootstrap5Theme()], NullLogger<FormBuilderThemes>.Instance), library));
        async Task<IReadOnlyList<FormCheckIssue>> CheckAsync(string? theme) =>
            await check.CheckAsync(Form([], theme is null ? "" : $$$""","settings":{"appearance":{"theme":"{{{theme}}}"}}"""), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((await CheckAsync("gone")).Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[] { (FormCheckSeverity.Warning, ThemeFormCheck.ThemeMissing, "$.settings.appearance.theme") }));
            Assert.That(await CheckAsync("bootstrap5"), Is.Empty, "a built-in theme, which has no row");
            Assert.That(await CheckAsync("brand"), Is.Empty, "a theme of the library");
            Assert.That(await CheckAsync(null), Is.Empty, "the site's default theme");
        });
    }

    [Test]
    public async Task ARedirect_ToAPageThatIsGoneOrNotPublished_IsAWarning()
    {
        var definition = Form(
            [],
            $$$""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","kind":"redirect","redirect":{"content":"{{{Published}}}"}},{"key":"60000000-0000-4000-8000-000000000002","kind":"redirect","redirect":{"content":"{{{Unpublished}}}"}},{"key":"60000000-0000-4000-8000-000000000003","kind":"redirect","redirect":{"content":"{{{GonePage}}}"}}]""");

        var issues = await References().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.RedirectUnpublished, "$.outcomes[1].redirect.content"),
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.ContentMissing, "$.outcomes[2].redirect.content"),
        }));
    }

    [Test]
    public async Task ContentAndMedia_TheSiteLacks_AreWarnings_InOptionsImagesAndInTheSettingsThatPickThem()
    {
        var definition = Form(
            [
                $$$"""{"key":"30000000-0000-4000-8000-000000000001","type":"image","settings":{"alt":{"":"A"},"media":"{{{GoneMedia}}}"}}""",
                $$$"""{"key":"30000000-0000-4000-8000-000000000002","type":"image","settings":{"alt":{"":"B"},"media":"{{{Picture}}}"}}""",
                $$$"""{"key":"30000000-0000-4000-8000-000000000003","type":"radio","alias":"pick","label":{"":"Pick"},"options":{"items":[{"value":"a","image":"{{{GoneMedia}}}"},{"value":"b","image":"{{{Picture}}}"}]}}""",
            ],
            $$$""","actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"test.page","settings":{"cover":{"image":"{{{GoneMedia}}}"},"page":"{{{GonePage}}}","folder":"{{{GoneMedia}}}","pages":["{{{Published}}}","{{{GonePage}}}"]}},{"key":"50000000-0000-4000-8000-000000000002","type":"test.page","settings":{"page":"{{{Unpublished}}}","folder":"{{{Picture}}}"}}]""");

        var issues = await References().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.MediaMissing, Fields + "[0].settings.media"),
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.MediaMissing, Fields + "[2].options.items[0].image"),
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.ContentMissing, "$.actions[0].settings.page"),
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.MediaMissing, "$.actions[0].settings.folder"),
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.ContentMissing, "$.actions[0].settings.pages[1]"),
            (FormCheckSeverity.Warning, SiteReferencesFormCheck.MediaMissing, "$.actions[0].settings.cover.image"),
        }));
    }

    [Test]
    public async Task MemberGroups_TheSiteLacks_AreWarnings_ByKeyInWhoMayFillItIn_AndByKeyOrExactNameInTheRules()
    {
        var definition = Form(
            [
                $$$"""{"key":"30000000-0000-4000-8000-000000000001","type":"text","alias":"topic","label":{"":"Topic"},"rules":[{"key":"40000000-0000-4000-8000-000000000001","effect":"show","match":"any","conditions":[{"context":"memberGroup","op":"contains","value":"Silver"},{"match":"all","conditions":[{"context":"memberGroup","op":"isOneOf","value":["gold","Bronze"]}]},{"context":"memberGroup","op":"contains","value":"{{{Gold}}}"},{"context":"memberGroup","op":"contains","value":"{{{GoneGroup}}}"}]}]}""",
            ],
            $$$""","outcomes":[{"key":"60000000-0000-4000-8000-000000000001","rules":[{"key":"40000000-0000-4000-8000-000000000002","effect":"use","conditions":[{"context":"memberGroup","op":"contains","value":"Gold"}]}]}],"settings":{"availability":{"members":"groups","memberGroups":["{{{Gold}}}","{{{GoneGroup}}}"]}}""");

        var issues = await References().CheckAsync(definition, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
            {
                (FormCheckSeverity.Warning, SiteReferencesFormCheck.MemberGroupMissing, "$.settings.availability.memberGroups[1]"),
                (FormCheckSeverity.Warning, SiteReferencesFormCheck.MemberGroupMissing, Fields + "[0].rules[0].conditions[0].value"),
                (FormCheckSeverity.Warning, SiteReferencesFormCheck.MemberGroupMissing, Fields + "[0].rules[0].conditions[1].conditions[0].value[0]"),
                (FormCheckSeverity.Warning, SiteReferencesFormCheck.MemberGroupMissing, Fields + "[0].rules[0].conditions[1].conditions[0].value[1]"),
                (FormCheckSeverity.Warning, SiteReferencesFormCheck.MemberGroupMissing, Fields + "[0].rules[0].conditions[3].value"),
            }));
            Assert.That(issues[1].Message, Does.Contain("'Silver'"));
            Assert.That(issues[2].Message, Does.EndWith("names are compared exactly, and the site has 'Gold'."), "the website compares a member's groups exactly");
            Assert.That(issues[4].Message, Does.Contain("no member group with this key"), "a key names a group, as on the website; Gold's key is none of these");
        });
    }

    [Test]
    public async Task AFormThatNamesNoMemberGroup_NeverAsksForThem()
    {
        var groups = Substitute.For<IMemberGroupService>();

        _ = await References(groups).CheckAsync(Form(["""{"key":"30000000-0000-4000-8000-000000000001","type":"text","alias":"topic","label":{"":"Topic"}}"""]), CancellationToken.None);

        await groups.DidNotReceive().GetAllAsync();
    }

    [Test]
    public void EveryLocalisedText_IsFound_WithItsPath_InTheFormsOrder()
    {
        var definition = Form(
            [
                """{"key":"30000000-0000-4000-8000-000000000001","type":"radio","alias":"pick","label":{"":"L"},"help":{"":"H"},"placeholder":{"":"P"},"messages":{"required":{"":"R"}},"options":{"items":[{"value":"a","label":{"":"A"},"description":{"":"D"}}]}}""",
                """{"key":"30000000-0000-4000-8000-000000000002","type":"text","alias":"code","label":{"":"C"},"validation":[{"kind":"pattern","pattern":"[a-z]+","message":{"":"M"}}]}""",
                """{"key":"30000000-0000-4000-8000-000000000003","type":"grid","alias":"rate","label":{"":"G"},"rows":[{"key":"30000000-0000-4000-8000-000000000004","label":{"":"Row"}}],"options":{"items":[{"value":"1"}]}}""",
                """{"key":"30000000-0000-4000-8000-000000000005","type":"heading","settings":{"text":{"":"Head","de":"Kopf"},"level":2}}""",
                """{"key":"30000000-0000-4000-8000-000000000006","type":"repeater","alias":"kids","label":{"":"K"},"fields":[{"key":"30000000-0000-4000-8000-000000000007","type":"text","alias":"kid","label":{"":"Kid"}}]}""",
            ],
            ""","validationRules":[{"key":"45000000-0000-4000-8000-000000000001","field":"30000000-0000-4000-8000-000000000002","message":{"":"V"},"conditions":[{"field":"30000000-0000-4000-8000-000000000002","op":"isEmpty"}]}],"outcomes":[{"key":"60000000-0000-4000-8000-000000000001","message":{"":"<p>T</p>"}}],"settings":{"submission":{"submitLabel":{"":"S"},"nextLabel":{"":"N"},"backLabel":{"":"B"}},"availability":{"closedMessage":{"":"<p>C</p>"}}}""",
            pageTexts: ""","title":{"":"Page"},"description":{"":"<p>About</p>"}""");

        var found = FormTexts.Of(definition, Types);

        Assert.That(found.Select(text => text.Path), Is.EqualTo(new[]
        {
            "$.pages[0].title", "$.pages[0].description",
            Fields + "[0].label", Fields + "[0].help", Fields + "[0].placeholder", Fields + "[0].messages.required", Fields + "[0].options.items[0].label", Fields + "[0].options.items[0].description",
            Fields + "[1].label", Fields + "[1].validation[0].message",
            Fields + "[2].label", Fields + "[2].rows[0].label",
            Fields + "[3].settings.text",
            Fields + "[4].label", Fields + "[4].fields[0].label",
            "$.validationRules[0].message", "$.outcomes[0].message",
            "$.settings.submission.submitLabel", "$.settings.submission.nextLabel", "$.settings.submission.backLabel", "$.settings.availability.closedMessage",
        }));
    }

    [Test]
    public async Task AText_ThatNamesADictionaryItemWithoutAText_IsAWarning_ForTheLanguagesWhoseVisitorsSeeIt()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-US", isDefault: true);
        umbraco.Language("de-DE");
        umbraco.Language("fr-FR", fallback: "de-DE");
        umbraco.Item("form.title", new Dictionary<string, string> { ["de-DE"] = "Kontakt" });
        umbraco.Item("form.ok", new Dictionary<string, string> { ["en-US"] = "OK", ["de-DE"] = "OK" });
        var definition = Form(
            [
                """{"key":"30000000-0000-4000-8000-000000000001","type":"text","alias":"topic","label":{"":"#form.gone"},"help":{"":"Plain","de":"#form.ok"}}""",
                """{"key":"30000000-0000-4000-8000-000000000002","type":"radio","alias":"pick","label":{"":"Pick"},"options":{"items":[{"value":"a","label":{"":"#Form.Gone","de":"Ja"}}]}}""",
                """{"key":"30000000-0000-4000-8000-000000000003","type":"heading","settings":{"text":{"":"#form.gone"},"level":2}}""",
                """{"key":"30000000-0000-4000-8000-000000000004","type":"heading","settings":{"text":"#form.gone","level":2}}""",
            ],
            ""","settings":{"submission":{"submitLabel":{"":"#form.ok"}}}""",
            pageTexts: ""","title":{"":"#form.title"}""");

        var issues = await new DictionaryFormCheck(Types, new MergeTagDictionary(umbraco.Dictionary, umbraco.Languages), umbraco.Languages).CheckAsync(definition, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
            {
                (FormCheckSeverity.Warning, DictionaryFormCheck.DictionaryTextMissing, "$.pages[0].title['']"),
                (FormCheckSeverity.Warning, DictionaryFormCheck.DictionaryTextMissing, Fields + "[0].label['']"),
                (FormCheckSeverity.Warning, DictionaryFormCheck.DictionaryTextMissing, Fields + "[1].options.items[0].label['']"),
                (FormCheckSeverity.Warning, DictionaryFormCheck.DictionaryTextMissing, Fields + "[2].settings.text['']"),
            }));
            Assert.That(issues[0].Message, Is.EqualTo("Umbraco's dictionary has no text for 'form.title' in en-US, so visitors there see the text as it is written."), "German visitors see the item's text, French ones through their language's fallback, English ones none of the default language");
            Assert.That(issues[1].Message, Does.Contain("in en-US, de-DE, fr-FR, so"));
            Assert.That(issues[2].Message, Does.Contain("'Form.Gone' in en-US, fr-FR, so"), "German visitors see their own text");
            Assert.That(umbraco.DictionaryReads, Is.EqualTo(3), "one read per language; a setting's single text is the settings check's to refuse, and the page shows none of it");
        });
    }

    [Test]
    public async Task AFormWhoseTextsNameNoDictionaryItem_ReadsNothing()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-US", isDefault: true);

        var issues = await new DictionaryFormCheck(Types, new MergeTagDictionary(umbraco.Dictionary, umbraco.Languages), umbraco.Languages)
            .CheckAsync(Form(["""{"key":"30000000-0000-4000-8000-000000000001","type":"text","alias":"topic","label":{"":"#"}}"""]), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(issues, Is.Empty, "'#' alone names no item");
            Assert.That(umbraco.DictionaryReads, Is.Zero);
            await umbraco.Languages.DidNotReceive().GetAllAsync();
        });
    }

    // The references check over a site with a published page, a page in the tree that is not published, a media item and
    // the member group Gold.
    private static SiteReferencesFormCheck References(IMemberGroupService? groups = null)
    {
        var documents = Substitute.For<IDocumentNavigationQueryService>();
        documents.TryGetParentKey(Arg.Any<Guid>(), out Arg.Any<Guid?>()).Returns(call => call.ArgAt<Guid>(0) == Published || call.ArgAt<Guid>(0) == Unpublished);
        var media = Substitute.For<IMediaNavigationQueryService>();
        media.TryGetParentKey(Arg.Any<Guid>(), out Arg.Any<Guid?>()).Returns(call => call.ArgAt<Guid>(0) == Picture);
        var published = Substitute.For<IPublishedContentCache>();
        var page = Substitute.For<IPublishedContent>();
        published.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<bool?>()).Returns(call => Task.FromResult(call.ArgAt<Guid>(0) == Published ? page : null));
        if (groups is null)
        {
            var gold = Substitute.For<IMemberGroup>();
            gold.Key.Returns(Gold);
            gold.Name.Returns("Gold");
            groups = Substitute.For<IMemberGroupService>();
            groups.GetAllAsync().Returns(Task.FromResult<IEnumerable<IMemberGroup>>([gold]));
        }

        return new SiteReferencesFormCheck(Types, new ActionTypeRegistry([new PageType()]), documents, media, published, groups);
    }

    // A form of one page and group with these fields, and anything after the pages.
    private static FormDefinition Form(string[] fields, string rest = "", string pageTexts = "") =>
        DefinitionFixtures.Read(
            "{\"schemaVersion\":1,\"key\":\"f0000000-0000-4000-8000-000000000001\",\"pages\":[{\"key\":\"10000000-0000-4000-8000-000000000001\""
            + pageTexts + ",\"groups\":[{\"key\":\"20000000-0000-4000-8000-000000000001\",\"fields\":[" + string.Join(",", fields) + "]}]}]" + rest + "}");

    private sealed class PageSettings
    {
        [FormBuilderSetting("Page", Kind = FieldSettingKind.Key, Editor = "documentPicker")]
        public Guid? Page { get; init; }

        [FormBuilderSetting("Folder", Kind = FieldSettingKind.Key, Editor = "mediaPicker")]
        public Guid? Folder { get; init; }

        [FormBuilderSetting("Pages", Kind = FieldSettingKind.Key, Editor = "contentPicker")]
        public IReadOnlyList<Guid> Pages { get; init; } = [];

        [FormBuilderSetting("Cover", Kind = FieldSettingKind.Nested)]
        public PageCover? Cover { get; init; }
    }

    private sealed class PageCover
    {
        [FormBuilderSetting("Image", Kind = FieldSettingKind.Key, Editor = "mediaPicker")]
        public Guid? Image { get; init; }
    }

    // An action type whose settings pick a page, a media folder and pages.
    private sealed class PageType() : FormBuilderActionTypeBase<PageSettings>(new ActionTypeDeclaration { Alias = "test.page", Name = "Page", Icon = "icon-document" })
    {
        protected override Task<ActionResult> RunAsync(PageSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }
}
