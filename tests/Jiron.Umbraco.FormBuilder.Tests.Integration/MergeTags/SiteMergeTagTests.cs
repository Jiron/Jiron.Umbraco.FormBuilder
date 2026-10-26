using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.MergeTags;

/// <summary>
/// A real site whose composers add merge-tag sources and filters through <c>builder.JironFormBuilder()</c> (ADR-0065),
/// one before the core composer and one after it: start-up passes, and the service renders and offers them beside the
/// built-in ones.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SiteMergeTagTests : UmbracoIntegrationTest
{
    /// <summary>Every test's site turns the composers on and has a greeting.</summary>
    public SiteMergeTagTests()
    {
        InMemoryConfiguration[SiteMergeTags.Switch] = "true";
        InMemoryConfiguration[GreetingMergeTagSource.GreetingKey] = "Hello <friend>";
    }

    [Test]
    public async Task TheSitesComposers_AddSourcesAndAFilter_BeforeAndAfterTheCoreComposer_AndTheSiteStarts()
    {
        var service = GetRequiredService<IFormBuilderMergeTagService>();
        var form = new FormDefinition { Key = Guid.Parse("f0000000-0000-4000-8000-000000000001") };
        var scope = new MergeTagScope { Form = form, FormName = "Contact", FormAlias = "contact", Culture = "en-GB" };

        var rendered = await service.RenderAsync("{{ greeting.text | shout }} {{ season.name }} {{ form.alias | shout }}", MergeTagPlace.Mail, MergeTagContext.Html, scope);

        Assert.Multiple(() =>
        {
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing);
            Assert.That(rendered.Text, Is.EqualTo("HELLO &lt;FRIEND&gt;! spring CONTACT!"));
            Assert.That(rendered.Problems, Is.Empty);
            Assert.That(service.Paths(MergeTagPlace.Mail, form).Select(path => path.Path), Does.Contain("form.name").And.Contain("greeting.text"));
            Assert.That(GetRequiredService<FormBuilderMergeTagSourceCollection>().Select(source => source.Name), Is.EqualTo(new[] { "greeting", "season" }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => builder.AddComposers();
}
