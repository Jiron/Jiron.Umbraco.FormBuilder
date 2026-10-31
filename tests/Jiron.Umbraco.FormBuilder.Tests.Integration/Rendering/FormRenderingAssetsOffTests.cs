using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A site that loads the forms' assets itself (section 4.8, ROADMAP 6.8a): with <c>Rendering:IncludeAssetsAutomatically</c>
/// off the site starts, and a placed form writes no tags unless its placement asks for them, through the tag helper or
/// the view component, while a <c>&lt;jfb-assets&gt;</c> in the layout still writes them.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingAssetsOffTests : RenderingTestBase
{
    [Test]
    public async Task WithTheAssetsOff_APlacementWritesNone_UnlessItAsksForThem()
    {
        _ = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("tag-helper", "component", "assets-asked", "assets-asked-component", "head-assets");
        var (_, tag) = await PageAsync("tag-helper");
        var (_, component) = await PageAsync("component");
        var (askedHtml, asked) = await PageAsync("assets-asked");
        var (_, askedByTheComponent) = await PageAsync("assets-asked-component");
        var (_, head) = await PageAsync("head-assets");

        Assert.Multiple(() =>
        {
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing, "section 4.8's Rendering section starts the site");
            Assert.That(GetRequiredService<IOptions<FormBuilderOptions>>().Value.Rendering.IncludeAssetsAutomatically, Is.False);
            foreach (var (name, document) in new[] { ("the tag helper", tag), ("the view component", component) })
            {
                Assert.That(document.QuerySelectorAll("form.jfb-form"), Has.Length.EqualTo(1), name);
                Assert.That(document.QuerySelectorAll("script[type=module], link[rel=stylesheet]"), Is.Empty, $"{name}: no assets the placement did not ask for");
            }

            Assert.That(asked.QuerySelectorAll("form.jfb-form"), Has.Length.EqualTo(2));
            Assert.That(asked.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "a placement that asks gets them, once per page");
            Assert.That(askedHtml.IndexOf("type=\"module\"", StringComparison.Ordinal), Is.LessThan(askedHtml.IndexOf("<form", StringComparison.Ordinal)), "before the form");
            Assert.That(askedByTheComponent.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "and so does the view component's");
            Assert.That(head.Head!.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "a <jfb-assets> in the layout still writes them");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        configuration[FormBuilderOptions.SectionName + ":Rendering:IncludeAssetsAutomatically"] = "false";
    }
}
