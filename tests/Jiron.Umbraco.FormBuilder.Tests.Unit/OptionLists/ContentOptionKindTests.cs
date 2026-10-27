using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists.OptionListTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// What content lists do apart from a site's content (ADR-0075): the key of what they keep, their own checks at the
/// edges of a property's alias, the most pages they read, values and labels from properties that hold several items,
/// the default language for no culture, and a root that is gone. The site's own pages are the integration tests'.
/// </summary>
[TestFixture]
public sealed class ContentOptionKindTests
{
    private static readonly Guid Root = Guid.Parse("76000000-0000-4000-8000-000000000001");
    private static readonly Guid TypeA = Guid.Parse("76000000-0000-4000-8000-00000000000a");
    private static readonly Guid TypeB = Guid.Parse("76000000-0000-4000-8000-00000000000b");

    [Test]
    public void TheKeyOfWhatIsKept_NamesEverySettingTheRootAndTheCulture_ButNotTheDocumentTypesOrder()
    {
        var key = OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB] }, Root, "en");
        string[] others =
        [
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB] }, Guid.NewGuid(), "en"),
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB] }, Root, "de"),
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA] }, Root, "en"),
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB], Descendants = true }, Root, "en"),
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB], ValueFrom = "name" }, Root, "en"),
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB], LabelFrom = "title" }, Root, "en"),
            OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeA, TypeB], Order = ContentOptionOrder.Label }, Root, "en"),
        ];
        var split = OptionSiteCacheKeys.Content(new ContentOptionListSettings { ValueFrom = "ab", LabelFrom = "c" }, Root, "en");
        var otherSplit = OptionSiteCacheKeys.Content(new ContentOptionListSettings { ValueFrom = "a", LabelFrom = "bc" }, Root, "en");

        Assert.Multiple(() =>
        {
            Assert.That(OptionSiteCacheKeys.Content(new ContentOptionListSettings { DocumentTypes = [TypeB, TypeA] }, Root, "en"), Is.EqualTo(key), "the types in any order");
            Assert.That(others, Has.None.EqualTo(key));
            Assert.That(others, Is.Unique);
            Assert.That(split, Is.Not.EqualTo(otherSplit), "each text with its length");
            Assert.That(key, Does.StartWith(OptionSiteCacheKeys.ContentPrefix));
            Assert.That(OptionSiteCacheKeys.Loading(key), Is.EqualTo(key + ".loading"));
            Assert.That(OptionSiteCacheKeys.DataType(TypeA), Is.EqualTo(OptionSiteCacheKeys.DataTypePrefix + TypeA.ToString("D")));
            Assert.That(OptionSiteCacheKeys.Duration, Is.EqualTo(TimeSpan.FromHours(12)));
        });
    }

    [Test]
    public void TheValueAndTheLabel_AreAPropertysAliasAsUmbracoWritesOne_OrTheirWords()
    {
        var content = new OptionListTestSite().Kinds.Find(OptionListKind.Content)!;
        List<(string Code, string Path)> Check(string config)
        {
            var problems = new DefinitionProblems();
            content.Check(SettingsObject.Parse(config), "$.config", "A content list", problems);
            return [.. problems.ToReadOnlyList().Select(problem => (problem.Code, problem.Path))];
        }

        var longest = "a" + new string('b', 254);

        Assert.Multiple(() =>
        {
            Assert.That(Check("""{"valueFrom":"name","labelFrom":"name"}"""), Is.Empty);
            Assert.That(Check("""{"valueFrom":"_code","labelFrom":"event_Title2"}"""), Is.Empty);
            Assert.That(Check($$"""{"valueFrom":"{{longest}}"}"""), Is.Empty, "255 characters");
            Assert.That(Check($$"""{"valueFrom":"{{longest}}c"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.valueFrom") }), "256 characters");
            Assert.That(Check("""{"valueFrom":"1code"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.valueFrom") }));
            Assert.That(Check("""{"valueFrom":"code "}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.valueFrom") }));
            Assert.That(Check("""{"labelFrom":"key"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.labelFrom") }));
            Assert.That(Check("""{"labelFrom":"a.b"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.labelFrom") }));
            Assert.That(Check("""{"order":"newest"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.order") }), "the tree or the labels");
            Assert.That(Check("""{"documentTypes":["76000000-0000-4000-8000-00000000000a","76000000-0000-4000-8000-00000000000a"]}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.documentTypes[1]") }), "each type once");
        });
    }

    [Test]
    public async Task AContentSource_ReadsAtMostOneMorePageThanAFieldOffers()
    {
        var site = new OptionListTestSite();
        var page = site.Umbraco.Page(Guid.NewGuid(), "Same");
        Children(site, Root, [.. Enumerable.Range(0, OptionListService.MaxOptions + 5).Select(_ => Guid.NewGuid())]);
        site.Umbraco.Contents.GetByIdAsync(Arg.Any<Guid>(), false).Returns(Task.FromResult<IPublishedContent?>(page));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Content, $$"""{"root":"{{Root}}"}"""), new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(site.Umbraco.Contents.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(site.Umbraco.Contents.GetByIdAsync)), Is.EqualTo(OptionListService.MaxOptions + 1));
            Assert.That(resolved.Options.Select(option => option.Label), Is.EqualTo(new[] { "Same" }), "the one key, once");
            Assert.That(site.Logs.Entries.Select(entry => entry.Message), Is.EqualTo(new[] { $"The field {FieldKey} left out 10000 options: blank, too long, not clean or repeated values, or more than 10,000" }));
        });
    }

    [Test]
    public async Task APropertyThatHoldsSeveralItems_IsALabel_ButNoValue_AndNoCultureIsTheDefaultLanguage()
    {
        var site = new OptionListTestSite();
        site.Umbraco.Language("en-US", isDefault: true);
        var concert = site.Umbraco.Page(
            Guid.Parse("76000000-0000-4000-8000-000000000011"),
            new Dictionary<string, string> { ["en-US"] = "Concert", ["de-DE"] = "Konzert" },
            properties: new Dictionary<string, object?> { ["tags"] = new[] { "Music", "Evening" }, ["free"] = true, ["code"] = "C1" });
        var fair = site.Umbraco.Page(Guid.Parse("76000000-0000-4000-8000-000000000012"), "Fair", properties: new Dictionary<string, object?> { ["code"] = "F1" });
        var talk = site.Umbraco.Page(Guid.Parse("76000000-0000-4000-8000-000000000013"), "Talk", properties: new Dictionary<string, object?> { ["tags"] = new[] { "Solo" }, ["code"] = "T1" });
        Children(site, Root, [concert.Key, fair.Key, talk.Key]);
        var options = site.Options();

        var tagged = await options.GetAsync(Field(OptionSource.Content, $$"""{"root":"{{Root}}","valueFrom":"code","labelFrom":"tags"}"""), new FormBuilderOptionContext(string.Empty));
        var byTags = await options.GetAsync(Field(OptionSource.Content, $$"""{"root":"{{Root}}","valueFrom":"tags"}"""), new FormBuilderOptionContext("en-US"));
        var byYesNo = await options.GetAsync(Field(OptionSource.Content, $$"""{"root":"{{Root}}","valueFrom":"code","labelFrom":"free"}"""), new FormBuilderOptionContext("de-DE"));

        Assert.Multiple(() =>
        {
            Assert.That(tagged.Options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("C1", "Music, Evening"), ("F1", "Fair"), ("T1", "Solo") }), "in English, the default language; a page without the property shows its name");
            Assert.That(byTags.Options, Is.Empty, "a value is one item, not a list, even of one");
            Assert.That(byYesNo.Options.Select(option => option.Label), Is.EqualTo(new[] { "Ja", "Fair", "Talk" }), "as merge tags show a property");
        });
    }

    [Test]
    public async Task ARootThatIsGone_MakesTheSourceFail_AndIsLoggedWithoutValues()
    {
        var site = new OptionListTestSite();

        var resolved = await site.Options().GetAsync(Field(OptionSource.Content, $$"""{"root":"{{Root}}"}"""), new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.Logs.Entries.Select(entry => (entry.Level, entry.Message.Split(". ")[0])), Is.EqualTo(new[]
            {
                (LogLevel.Error, $"The content source of the field {FieldKey} failed with System.InvalidOperationException, so it offers no options"),
            }));
        });
    }

    // The tree under a page, which the site lacks unless a test adds it; no page needs a member to sign in.
    private static void Children(OptionListTestSite site, Guid parent, Guid[] children)
    {
        site.Umbraco.Navigation.TryGetChildrenKeys(parent, out Arg.Any<IEnumerable<Guid>>()).Returns(call =>
        {
            call[1] = children;
            return true;
        });
        site.PublicAccess.IsProtected(Arg.Any<string>()).Returns(Attempt<PublicAccessEntry?>.Fail());
    }
}
