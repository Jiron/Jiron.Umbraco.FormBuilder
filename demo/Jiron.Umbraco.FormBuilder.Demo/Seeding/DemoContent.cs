using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>
/// The demo's texts (ROADMAP 3.3): Umbraco dictionary items for the views, and the pages of the fictional Lindenhall
/// Library in English and German. They live in <c>DemoContent.json</c>, embedded in this assembly, because code stays
/// ASCII and the German texts do not.
/// </summary>
internal sealed record DemoContent(
    IReadOnlyDictionary<string, DemoTranslation> Dictionary,
    DemoPage Home,
    IReadOnlyList<DemoPage> Pages)
{
    public const string HomeType = "demoHome";
    public const string PageType = "demoPage";
    public const string MembersType = "demoMembers";
    public const string LoginType = "demoLogin";
    public const string LegalType = "demoLegalPage";
    public const string SpecimenType = "demoSpecimen";
    public const string ThanksType = "demoThanks";
    public const string TextBlockType = "demoTextBlock";
    public const string HighlightBlockType = "demoHighlightBlock";

    private const string ResourceName = "Jiron.Umbraco.FormBuilder.Demo.DemoContent.json";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The one page below the home page that has the given document type.</summary>
    public DemoPage PageOfType(string type) => Pages.Single(page => page.Type == type);

    /// <summary>Reads the texts from the embedded <c>DemoContent.json</c>.</summary>
    public static DemoContent Load()
    {
        using var stream = typeof(DemoContent).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The demo's texts ({ResourceName}) are not embedded.");
        return JsonSerializer.Deserialize<DemoContent>(stream, Options)
            ?? throw new InvalidOperationException($"The demo's texts ({ResourceName}) are empty.");
    }
}

/// <summary>A text in English and German.</summary>
internal sealed record DemoTranslation(string En, string De);

/// <summary>
/// A page with its fixed key, document type, texts per language, blocks, the alias of the form it shows and the topic it
/// gives that form as a context value (ADR-0124).
/// </summary>
internal sealed record DemoPage(Guid Key, string Type, DemoPageTexts En, DemoPageTexts De, IReadOnlyList<DemoBlock> Blocks, string? Form = null, string? FormTopic = null)
{
    public DemoPageTexts Texts(bool german) => german ? De : En;

    /// <summary>Whether a block picks a form, so the page waits for the forms (ADR-0136).</summary>
    public bool HoldsFormBlocks => Blocks.Any(block => block.Form is not null);
}

/// <summary>A page's texts in one language; the newsletter texts belong to the home page only.</summary>
internal sealed record DemoPageTexts(
    string Name,
    string Heading,
    string Lede,
    string Body,
    string? NewsletterHeading = null,
    string? NewsletterText = null);

/// <summary>
/// A block of the page's block list: its element type and its texts per language, or, for the form block of
/// "Create a form block for me", the alias of the form it picks (ADR-0136).
/// </summary>
internal sealed record DemoBlock(string Type, DemoBlockTexts? En = null, DemoBlockTexts? De = null, string? Form = null)
{
    public DemoBlockTexts Texts(bool german) => (german ? De : En)
        ?? throw new InvalidOperationException($"The demo's {Type} block has no texts.");
}

/// <summary>A block's heading and text (markup for a text block, plain text for a highlight).</summary>
internal sealed record DemoBlockTexts(string Heading, string Text);
