using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Integration.FieldTypes;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.MergeTags;

/// <summary>
/// A real site whose composers add merge-tag sources and filters through <c>builder.JironFormBuilder()</c> (ADR-0065),
/// one before the core composer and one after it: start-up passes, and the service renders and offers them beside the
/// built-in ones. Its own field type shows its answers as it writes them (ADR-0066).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SiteMergeTagTests : UmbracoIntegrationTest
{
    /// <summary>Every test's site turns the composers on, the field types' too, and has a greeting.</summary>
    public SiteMergeTagTests()
    {
        InMemoryConfiguration[SiteMergeTags.Switch] = "true";
        InMemoryConfiguration[SiteFieldTypes.Switch] = "true";
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

    [Test]
    public async Task TheAnswers_ShowAsTheirTypesWriteThem_TheSitesOwnTypeToo()
    {
        var colour = new FormField { Key = Guid.Parse("72000000-0000-4000-8000-000000000001"), Type = "acme.colour", Alias = "colour", Label = new LocalizedText("Farbe") };
        var country = new FormField { Key = Guid.Parse("72000000-0000-4000-8000-000000000002"), Type = "country", Alias = "country", Label = new LocalizedText("Land") };
        var form = new FormDefinition
        {
            Key = Guid.Parse("f0000000-0000-4000-8000-000000000001"),
            Pages = [new FormPage { Key = Guid.Parse("72000000-0000-4000-8000-000000000003"), Groups = [new FormGroup { Key = Guid.Parse("72000000-0000-4000-8000-000000000004"), Fields = [colour, country] }] }],
        };
        var scope = new MergeTagScope
        {
            Form = form,
            FormName = "Kontakt",
            FormAlias = "contact",
            Culture = "de-CH",
            Answers = new MergeTagAnswers([new MergeTagAnswer(colour.Key, [new FieldValue("red")]), new MergeTagAnswer(country.Key, [new FieldValue("CH")])]),
        };

        var rendered = await GetRequiredService<IFormBuilderMergeTagService>().RenderAsync(
            "{{ field.colour }} {{ field.country }}|{{ entry.answers }}", MergeTagPlace.Mail, MergeTagContext.Text, scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Red Schweiz|Farbe: Red\nLand: Schweiz"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => builder.AddComposers();
}
