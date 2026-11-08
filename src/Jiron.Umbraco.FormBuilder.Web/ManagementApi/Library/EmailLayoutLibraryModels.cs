using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A page of the library's email layouts.</summary>
internal sealed record EmailLayoutRowsResponseModel
{
    /// <summary>How many layouts the library holds.</summary>
    public required long Total { get; init; }

    /// <summary>The page's layouts, by name.</summary>
    public required IReadOnlyList<EmailLayoutRowResponseModel> Items { get; init; }
}

/// <summary>A row of the library's email layouts.</summary>
internal sealed record EmailLayoutRowResponseModel
{
    /// <summary>The layout's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Its look: <c>card</c> or <c>letter</c>.</summary>
    public required string Look { get; init; }

    /// <summary>Its accent and page colours in the light scheme, as mail draws them.</summary>
    public required IReadOnlyList<string> Swatch { get; init; }

    /// <summary>How many email actions name it, in forms' drafts or live versions.</summary>
    public required int UsedBy { get; init; }

    /// <summary>Whether it is the site's default layout.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>A row as the service gives it.</summary>
    public static EmailLayoutRowResponseModel Of(EmailLayoutRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var look = row.Design.Look == EmailLook.Letter ? EmailRenderer.LightColours with { Page = EmailRenderer.LightColours.Surface } : EmailRenderer.LightColours;
        return new EmailLayoutRowResponseModel
        {
            Key = row.Layout.Key,
            Alias = row.Layout.Alias,
            Name = row.Layout.Name,
            Look = EnumNames<EmailLook>.ByValue[row.Design.Look],
            Swatch = [row.Design.Light?.Accent ?? look.Accent!, row.Design.Light?.Page ?? look.Page!],
            UsedBy = row.UsedBy,
            IsDefault = row.IsDefault,
            UpdatedUtc = row.Layout.UpdatedUtc,
        };
    }
}

/// <summary>What the email layout editor needs besides a layout.</summary>
internal sealed record EmailLayoutEditorResponseModel
{
    /// <summary>The site's languages, the default first, which the header and footer have a text in.</summary>
    public required IReadOnlyList<OptionListLanguageResponseModel> Languages { get; init; }

    /// <summary>The colours mail takes in the light scheme where a layout sets none.</summary>
    public required EmailColoursResponseModel Light { get; init; }

    /// <summary>The colours mail takes in the dark scheme where a layout sets none.</summary>
    public required EmailColoursResponseModel Dark { get; init; }

    /// <summary>The key of the site's default layout; null for the built-in card.</summary>
    public Guid? DefaultLayout { get; init; }
}

/// <summary>A layout's colours as it holds them, each a hex colour or none for mail's own.</summary>
internal sealed record EmailColoursResponseModel
{
    /// <summary>Links, buttons and the header's line.</summary>
    public string? Accent { get; init; }

    /// <summary>Around the mail.</summary>
    public string? Page { get; init; }

    /// <summary>The mail's own background.</summary>
    public string? Surface { get; init; }

    /// <summary>The text.</summary>
    public string? Text { get; init; }

    /// <summary>Quieter text: the footer and labels.</summary>
    public string? Muted { get; init; }

    /// <summary>Dividers.</summary>
    public string? Rule { get; init; }

    /// <summary>Colours as mail holds them; null for none.</summary>
    public static EmailColoursResponseModel? Of(EmailColours? colours) => colours is null
        ? null
        : new EmailColoursResponseModel { Accent = colours.Accent, Page = colours.Page, Surface = colours.Surface, Text = colours.Text, Muted = colours.Muted, Rule = colours.Rule };
}

/// <summary>A layout's colours as its editor saves them, each a hex colour or none for mail's own.</summary>
internal sealed record EmailColoursRequestModel
{
    /// <summary>Links, buttons and the header's line.</summary>
    public string? Accent { get; init; }

    /// <summary>Around the mail.</summary>
    public string? Page { get; init; }

    /// <summary>The mail's own background.</summary>
    public string? Surface { get; init; }

    /// <summary>The text.</summary>
    public string? Text { get; init; }

    /// <summary>Quieter text: the footer and labels.</summary>
    public string? Muted { get; init; }

    /// <summary>Dividers.</summary>
    public string? Rule { get; init; }

    /// <summary>The colours as mail holds them.</summary>
    public EmailColours ToColours() => new() { Accent = Accent, Page = Page, Surface = Surface, Text = Text, Muted = Muted, Rule = Rule };
}

/// <summary>A layout's design as it is stored: its look, logo, colours, header and footer.</summary>
internal sealed record EmailLayoutDesignResponseModel
{
    /// <summary>The look: <c>card</c> or <c>letter</c>.</summary>
    public required string Look { get; init; }

    /// <summary>The media item the logo is; null for a logo by address or none.</summary>
    public Guid? LogoMedia { get; init; }

    /// <summary>The logo's address: absolute, or a path on the site; a media item's is read from it on save.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>The logo's alternative text per culture, the empty culture the default.</summary>
    public IReadOnlyDictionary<string, string>? LogoAlt { get; init; }

    /// <summary>The light scheme's colours.</summary>
    public EmailColoursResponseModel? Light { get; init; }

    /// <summary>The dark scheme's colours.</summary>
    public EmailColoursResponseModel? Dark { get; init; }

    /// <summary>The header's text per culture, simple HTML.</summary>
    public IReadOnlyDictionary<string, string>? Header { get; init; }

    /// <summary>The footer's text per culture, simple HTML.</summary>
    public IReadOnlyDictionary<string, string>? Footer { get; init; }

    /// <summary>A design as mail holds it.</summary>
    public static EmailLayoutDesignResponseModel Of(EmailLayoutDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new EmailLayoutDesignResponseModel
        {
            Look = EnumNames<EmailLook>.ByValue[design.Look],
            LogoMedia = design.LogoMedia,
            LogoUrl = design.LogoUrl,
            LogoAlt = design.LogoAlt,
            Light = EmailColoursResponseModel.Of(design.Light),
            Dark = EmailColoursResponseModel.Of(design.Dark),
            Header = design.Header,
            Footer = design.Footer,
        };
    }
}

/// <summary>A layout's design as its editor saves it: its look, logo, colours, header and footer.</summary>
internal sealed record EmailLayoutDesignRequestModel
{
    /// <summary>The look: <c>card</c> or <c>letter</c>.</summary>
    public required string Look { get; init; }

    /// <summary>The media item the logo is; null for a logo by address or none.</summary>
    public Guid? LogoMedia { get; init; }

    /// <summary>The logo's address: absolute, or a path on the site; a media item's is read from it on save.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>The logo's alternative text per culture, the empty culture the default.</summary>
    public IReadOnlyDictionary<string, string>? LogoAlt { get; init; }

    /// <summary>The light scheme's colours.</summary>
    public EmailColoursRequestModel? Light { get; init; }

    /// <summary>The dark scheme's colours.</summary>
    public EmailColoursRequestModel? Dark { get; init; }

    /// <summary>The header's text per culture, simple HTML.</summary>
    public IReadOnlyDictionary<string, string>? Header { get; init; }

    /// <summary>The footer's text per culture, simple HTML.</summary>
    public IReadOnlyDictionary<string, string>? Footer { get; init; }

    /// <summary>The design as mail holds it; a look that is none of mail's is refused at its path.</summary>
    public EmailLayoutDesign ToDesign() => new()
    {
        Look = EnumNames<EmailLook>.ByName.TryGetValue(Look ?? string.Empty, out var look)
            ? look
            : throw new Core.Forms.FormBuilderValidationException([new Core.Definitions.DefinitionProblem(Core.Definitions.DefinitionErrors.InvalidValue, "$.design.look", $"A look is one of {string.Join(", ", EnumNames<EmailLook>.All)}.")]),
        LogoMedia = LogoMedia,
        LogoUrl = LogoMedia is null && string.IsNullOrWhiteSpace(LogoUrl) ? null : LogoUrl,
        LogoAlt = LogoAlt,
        Light = Light?.ToColours(),
        Dark = Dark?.ToColours(),
        Header = Header,
        Footer = Footer,
    };
}

/// <summary>An email layout as its editor shows it.</summary>
internal sealed record EmailLayoutResponseModel
{
    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Its design.</summary>
    public required EmailLayoutDesignResponseModel Design { get; init; }

    /// <summary>The version a change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>A stored layout.</summary>
    public static EmailLayoutResponseModel Of(StoredEmailLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new EmailLayoutResponseModel
        {
            Key = layout.Key,
            Alias = layout.Alias,
            Name = layout.Name,
            Design = EmailLayoutDesignResponseModel.Of(EmailLayoutLibrary.DesignOf(layout.Layout)),
            ConcurrencyVersion = layout.ConcurrencyVersion,
            UpdatedUtc = layout.UpdatedUtc,
        };
    }
}

/// <summary>A layout as its editor saves it.</summary>
internal sealed record EmailLayoutRequestModel
{
    /// <summary>Its alias: a lower-case letter, then letters and digits.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name, 1 to 255 characters.</summary>
    public required string Name { get; init; }

    /// <summary>Its design.</summary>
    public required EmailLayoutDesignRequestModel Design { get; init; }

    /// <summary>The layout as the service takes it.</summary>
    public EmailLayoutInput ToInput() => new(Alias ?? string.Empty, Name ?? string.Empty, Design.ToDesign());
}

/// <summary>A layout to create.</summary>
internal sealed record CreateEmailLayoutRequestModel
{
    /// <summary>The key to create it under; none for a new one.</summary>
    public Guid? Key { get; init; }

    /// <summary>The layout.</summary>
    public required EmailLayoutRequestModel Layout { get; init; }
}

/// <summary>A change to a layout, based on the version read.</summary>
internal sealed record UpdateEmailLayoutRequestModel
{
    /// <summary>The version the change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>The layout.</summary>
    public required EmailLayoutRequestModel Layout { get; init; }
}

/// <summary>A copy's name.</summary>
internal sealed record DuplicateEmailLayoutRequestModel
{
    /// <summary>The copy's name, 1 to 255 characters.</summary>
    public required string Name { get; init; }
}

/// <summary>A design as the editor holds it, saved or not, to preview with a sample entry.</summary>
internal sealed record PreviewEmailLayoutRequestModel
{
    /// <summary>The design.</summary>
    public required EmailLayoutDesignRequestModel Design { get; init; }

    /// <summary>The culture of the mail, such as <c>de-DE</c>.</summary>
    public required string Culture { get; init; }
}

/// <summary>A sample entry's mail in a layout, or the problems that refuse the layout.</summary>
internal sealed record EmailLayoutPreviewResponseModel
{
    /// <summary>The mail's HTML, with its light and dark styles; empty when the layout is refused.</summary>
    public required string Html { get; init; }

    /// <summary>The problems a save would refuse, each at its path.</summary>
    public required IReadOnlyList<LibraryProblemResponseModel> Problems { get; init; }
}

/// <summary>A test mail in a saved layout.</summary>
internal sealed record TestEmailLayoutRequestModel
{
    /// <summary>The mail's culture, such as <c>de-DE</c>; none for the user's language.</summary>
    public string? Culture { get; init; }
}
