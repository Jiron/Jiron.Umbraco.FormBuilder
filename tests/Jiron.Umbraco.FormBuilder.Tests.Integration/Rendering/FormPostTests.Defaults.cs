using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// A default value that holds merge tags (ROADMAP 6.4h, ADR-0118): the server leaves its controls empty, the same for
// every visitor, and marks the field data-jfb-default where its controls start, on a first render and in a row just
// added, for the page to fill in; a default without tags is the server's own and carries no mark, and after a post the
// answers stand, unmarked.
public sealed partial class FormPostTests
{
    private const string DefaultsPage = "/post-defaults/";

    private static readonly FormField Source = Field(K(0xC0), "source", "text") with { DefaultValue = FieldDefaultValue.Of("""{{ query.ref | default: "web" }}""") };
    private static readonly FormField Greeting = Field(K(0xC1), "greeting", "text") with { DefaultValue = FieldDefaultValue.Of("Hello") };
    private static readonly FormField Signed = Field(K(0xC2), "signed", "text") with { DefaultValue = FieldDefaultValue.Of("{{ member.name }}") };
    private static readonly FormField Kid = Field(K(0xC4), "kid", "text") with { DefaultValue = FieldDefaultValue.Of("{{ site.name }} guest") };
    private static readonly FormField Kids = Field(K(0xC3), "kids", "repeater", """{"minRows":1,"maxRows":3}""") with { Fields = [Kid] };

    [Test]
    public async Task ADefaultWithTags_IsLeftEmptyAndMarkedWhereItsControlsStart_AndAfterAPostTheAnswersStand()
    {
        await PublishFormAsync("postDefaults", definition => WithFields(definition, Source, Greeting, Signed, Kids) with
        {
            Settings = definition.Settings with { Developer = definition.Settings.Developer with { QueryValues = ["ref"] } },
        });
        _ = await PagesAsync("post-defaults");
        var first = FormOf(await DocumentAsync(DefaultsPage), "jfb-1");
        var marked = Marked(first);
        var source = Value(first, "source") ?? string.Empty;
        var greeting = Value(first, "greeting") ?? string.Empty;
        Fill(first, "kids[0].kid", "Ada");
        var added = await MoveAsync(first, "add:kids", "jfb-1", DefaultsPage);

        Assert.Multiple(() =>
        {
            Assert.That(marked, Is.EqualTo(new[] { "source", "signed", "kids[0].kid" }), "a first render marks every field whose default holds a tag, a refused one too, and a row's");
            Assert.That(source, Is.Empty, "the server leaves it for the page, so the markup is the same for every visitor");
            Assert.That(greeting, Is.EqualTo("Hello"), "a default without tags is the server's own");
            Assert.That(Marked(added), Is.EqualTo(new[] { "kids[1].kid" }), "after a post only the row just added starts");
            Assert.That(Value(added, "kids[0].kid") ?? string.Empty, Is.EqualTo("Ada"));
            Assert.That(Value(added, "kids[1].kid") ?? string.Empty, Is.Empty);
            Assert.That(Value(added, "source") ?? string.Empty, Is.Empty, "the answer the post gave, empty, stands");
            Assert.That(Value(added, "greeting") ?? string.Empty, Is.EqualTo("Hello"));
        });
    }

    // The fields a form marks for the page to fill in, by the names their controls post.
    private static string[] Marked(IElement form) => [.. form.QuerySelectorAll("[data-jfb-default]").Select(field => field.GetAttribute("data-jfb-alias") ?? string.Empty)];
}
