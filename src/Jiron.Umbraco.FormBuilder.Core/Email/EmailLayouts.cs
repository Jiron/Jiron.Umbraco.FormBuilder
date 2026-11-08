using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>The two shapes a mail can take (ADR-0159).</summary>
internal enum EmailLook
{
    /// <summary>A bordered card on a quiet page, for mail that comes from a site.</summary>
    Card,

    /// <summary>Text on the page itself, for mail that reads as written by a person.</summary>
    Letter,
}

/// <summary>The colours of one colour scheme; a colour left out, or not written as <c>#rrggbb</c>, takes the look's own.</summary>
internal sealed record EmailColours
{
    /// <summary>Links.</summary>
    public string? Accent { get; init; }

    /// <summary>The page around the mail.</summary>
    public string? Page { get; init; }

    /// <summary>What the text stands on.</summary>
    public string? Surface { get; init; }

    /// <summary>The text.</summary>
    public string? Text { get; init; }

    /// <summary>The header and footer texts.</summary>
    public string? Muted { get; init; }

    /// <summary>Borders and rules.</summary>
    public string? Rule { get; init; }
}

/// <summary>What a layout of the library holds in its <c>layout</c> column (section 4.5, ADR-0159).</summary>
internal sealed record EmailLayoutDesign
{
    /// <summary>The shape.</summary>
    public EmailLook Look { get; init; }

    /// <summary>The logo's address: an absolute web address, or a path on the site, which mail puts under the site's base address; null for none.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>The media item the logo is, whose address the editor's save keeps in <see cref="LogoUrl"/> (ADR-0215); null for a logo by address.</summary>
    public Guid? LogoMedia { get; init; }

    /// <summary>What the logo says, per culture; without it, the sender's name.</summary>
    public IReadOnlyDictionary<string, string>? LogoAlt { get; init; }

    /// <summary>The colours when the reader's device is light.</summary>
    public EmailColours? Light { get; init; }

    /// <summary>The colours when the reader's device is dark.</summary>
    public EmailColours? Dark { get; init; }

    /// <summary>Rich text above the mail, per culture.</summary>
    public IReadOnlyDictionary<string, string>? Header { get; init; }

    /// <summary>Rich text below the mail, per culture.</summary>
    public IReadOnlyDictionary<string, string>? Footer { get; init; }
}

/// <summary>
/// The layouts a mail can name (ADR-0159): the built-in ones and those of the library. Nothing about a layout ever stops a
/// mail: one that is gone or cannot be read gives way to the built-in card.
/// </summary>
internal sealed partial class EmailLayouts(IEmailLayoutRepository layouts, ILogger<EmailLayouts> logger)
{
    /// <summary>The key of the built-in card.</summary>
    public static readonly Guid CardKey = new("6f0a2c1e-58b3-4d97-a3e1-2b7c9d40e611");

    /// <summary>The key of the built-in letter.</summary>
    public static readonly Guid LetterKey = new("c41d7e92-0b6f-4a38-8e25-71f3a6d8b902");

    /// <summary>Every built-in layout, by its key.</summary>
    public static readonly FrozenDictionary<Guid, EmailLayoutDesign> BuiltIn = new Dictionary<Guid, EmailLayoutDesign>
    {
        [CardKey] = new EmailLayoutDesign { Look = EmailLook.Card },
        [LetterKey] = new EmailLayoutDesign { Look = EmailLook.Letter },
    }.ToFrozenDictionary();

    /// <summary>How a layout's design is read and written: camel case, the look by its name.</summary>
    internal static readonly JsonSerializerOptions Json = Options();

    /// <summary>The key of the layout a mail naming this one gets: itself when built in or in the library, else the card.</summary>
    /// <param name="key">The layout named, or null for none.</param>
    /// <param name="stored">The keys of the library's layouts.</param>
    public static Guid KeyOf(Guid? key, IEnumerable<Guid> stored) =>
        key is { } named && (BuiltIn.ContainsKey(named) || stored.Contains(named)) ? named : CardKey;

    /// <summary>The layout with this key; null, an unknown key or one that cannot be read give the built-in card.</summary>
    public async Task<EmailLayoutDesign> FindAsync(Guid? key, CancellationToken cancellationToken)
    {
        if (key is not { } wanted)
        {
            return BuiltIn[CardKey];
        }

        if (BuiltIn.TryGetValue(wanted, out var builtIn))
        {
            return builtIn;
        }

        if (await layouts.GetAsync(wanted, cancellationToken) is not { } stored)
        {
            LogLayoutGone(logger, wanted);
            return BuiltIn[CardKey];
        }

        try
        {
            return JsonSerializer.Deserialize<EmailLayoutDesign>(stored.Layout, Json) ?? BuiltIn[CardKey];
        }
        catch (JsonException)
        {
            LogLayoutUnreadable(logger, wanted);
            return BuiltIn[CardKey];
        }
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder's email layout {Layout} no longer exists; the mail uses the built-in card")]
    private static partial void LogLayoutGone(ILogger logger, Guid layout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder's email layout {Layout} cannot be read; the mail uses the built-in card")]
    private static partial void LogLayoutUnreadable(ILogger logger, Guid layout);
}
