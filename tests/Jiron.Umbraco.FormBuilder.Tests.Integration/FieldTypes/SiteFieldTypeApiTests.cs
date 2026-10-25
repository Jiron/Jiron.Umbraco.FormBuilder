using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.FieldTypes;

/// <summary>
/// The field types the backoffice reads on a site whose composers change them (ADR-0055, ADR-0057): the site's colour
/// type comes after the built-in types with its own plain-text labels, and is not configured, with its reason, while the
/// site's configuration lacks its brand colours; the removed signature is not offered at all.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class SiteFieldTypeApiTests : ManagementApiTestBase
{
    /// <summary>Every test's site turns the composers on, and gives the colour type no brand colours.</summary>
    public SiteFieldTypeApiTests() => InMemoryConfiguration[SiteFieldTypes.Switch] = "true";

    [Test]
    public async Task ASitesTypeThatIsNotConfigured_ReachesTheBackoffice_WithItsReason()
    {
        var answer = await CallAsync("designer", HttpMethod.Get, "field-type");
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var types = answer.Body!["types"]!.AsArray().Select(type => type!.AsObject()).ToList();
        var colour = types[^1];

        Assert.Multiple(() =>
        {
            Assert.That(colour["alias"]!.GetValue<string>(), Is.EqualTo("acme.colour"), "after the built-in types");
            Assert.That(colour["availability"]!.ToJsonString(), Is.EqualTo(new JsonObject { ["isAvailable"] = false, ["reason"] = ColourFieldType.MissingSwatches }.ToJsonString()));
            Assert.That(colour["settings"]!.AsArray().Select(setting => $"{setting!["name"]} {setting["label"]}"), Is.EquivalentTo(new[] { "swatches Swatches", "source Source" }), "a site's type names its settings in its own words");
            Assert.That(types.Take(types.Count - 1).Select(type => (bool)type["availability"]!["isAvailable"]!), Is.All.True);
            Assert.That(types.Select(type => type["alias"]!.GetValue<string>()), Does.Not.Contain("signature"), "removed after the core composer");
        });
    }
}
