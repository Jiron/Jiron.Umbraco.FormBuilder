using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>What an email layout's editor saves (ADR-0215).</summary>
/// <param name="Alias">Its alias: a lower-case letter, then letters and digits.</param>
/// <param name="Name">Its name.</param>
/// <param name="Design">Its look, logo, colours, header and footer.</param>
internal sealed record EmailLayoutInput(string Alias, string Name, EmailLayoutDesign Design);

/// <summary>A saved layout, or the problems that refused it, each at its path.</summary>
/// <param name="Layout">The layout saved; null when refused or gone.</param>
/// <param name="Problems">Why it was refused.</param>
internal sealed record EmailLayoutResult(StoredEmailLayout? Layout, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>A row of the library's list of email layouts (ADR-0215).</summary>
/// <param name="Layout">The layout.</param>
/// <param name="Design">Its design as mail reads it.</param>
/// <param name="UsedBy">How many email actions name it, in forms' drafts or live versions.</param>
/// <param name="IsDefault">Whether it is the site's default layout.</param>
internal sealed record EmailLayoutRow(StoredEmailLayout Layout, EmailLayoutDesign Design, int UsedBy, bool IsDefault);

/// <summary>How a delete ended.</summary>
internal enum EmailLayoutDelete
{
    /// <summary>The layout is gone.</summary>
    Deleted,

    /// <summary>There was no such layout.</summary>
    NotFound,

    /// <summary>The layout is the site's default, which Settings > Email names; it stays.</summary>
    IsDefault,
}

/// <summary>
/// The library's email layouts as their screens work with them (ROADMAP 12.1, ADR-0215): the rows with how many email
/// actions name each, which forms those are as a user may see them, the changes a user makes, each checked and audited,
/// a duplicate under a free alias, the preview of a sample entry in a layout as it is, saved or not, and a test mail in a
/// saved one. A logo from the media library keeps its media item and the address the save reads from it.
/// </summary>
internal sealed partial class EmailLayoutLibrary(
    IEmailLayoutRepository repository,
    LibraryUsers users,
    ILibraryUseRepository uses,
    IFormBuilderSettingsService settings,
    IMediaService media,
    MediaUrlGeneratorCollection mediaUrls,
    EmailRenderer renderer,
    TestEmails tests,
    IAuditRepository audit)
{
    /// <summary>The audit action of a layout created, a duplicate's included.</summary>
    public const string CreatedAction = "emailLayout.created";

    /// <summary>The audit action of a layout saved.</summary>
    public const string SavedAction = "emailLayout.saved";

    /// <summary>The audit action of a layout deleted.</summary>
    public const string DeletedAction = "emailLayout.deleted";

    /// <summary>The audit action of a test mail sent in a layout.</summary>
    public const string TestSentAction = "emailLayout.testSent";

    /// <summary>The longest header, footer or logo text of a language.</summary>
    public const int MaxTextLength = 4000;

    /// <summary>The longest logo address.</summary>
    public const int MaxAddressLength = 2048;

    private const string EntityType = "emailLayout";

    /// <summary>A page of the layouts by name, each with how many email actions name it and whether it is the default.</summary>
    public async Task<Page<EmailLayoutRow>> RowsAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await repository.ListAsync(null, page, cancellationToken);
        var counts = await uses.UseCountsAsync(LibraryUseKind.EmailLayout, cancellationToken);
        var fallback = await DefaultKeyAsync(cancellationToken);
        return new Page<EmailLayoutRow>([.. rows.Items.Select(row => new EmailLayoutRow(row, DesignOf(row.Layout), counts.GetValueOrDefault(row.Key), row.Key == fallback))], rows.Total);
    }

    /// <summary>The layout of this key, or null.</summary>
    public Task<StoredEmailLayout?> GetAsync(Guid key, CancellationToken cancellationToken = default) => repository.GetAsync(key, cancellationToken);

    /// <summary>The key of the site's default layout, as Settings > Email names it; null for the built-in card.</summary>
    public async Task<Guid?> DefaultKeyAsync(CancellationToken cancellationToken = default) =>
        (await settings.GetAsync<EmailSettings>(cancellationToken: cancellationToken)).Value.DefaultLayout;

    /// <summary>Whether the layout is the default for all sites or a site's own (ADR-0215), which stays while it is.</summary>
    private async Task<bool> IsADefaultAsync(Guid key, CancellationToken cancellationToken)
    {
        if (await DefaultKeyAsync(cancellationToken) == key)
        {
            return true;
        }

        foreach (var site in await settings.SitesOfAsync<EmailSettings>(cancellationToken))
        {
            if ((await settings.GetAsync<EmailSettings>(site, cancellationToken)).Value.DefaultLayout == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The forms whose email actions name the layout, as the user may see them.</summary>
    public Task<LibraryItemUsers> UsersAsync(AccessUser user, Guid key, CancellationToken cancellationToken = default) =>
        users.ForAsync(user, LibraryUseKind.EmailLayout, key, cancellationToken);

    /// <summary>A stored design as mail reads it; one that is not of its shape is the built-in card.</summary>
    public static EmailLayoutDesign DesignOf(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EmailLayoutDesign>(json, EmailLayouts.Json) ?? EmailLayouts.BuiltIn[EmailLayouts.CardKey];
        }
        catch (JsonException)
        {
            return EmailLayouts.BuiltIn[EmailLayouts.CardKey];
        }
    }

    /// <summary>
    /// The problems of a layout as an editor holds it, each at its path: an alias outside the grammar, a missing or long
    /// name, a colour that is no hex colour, a logo address that is neither absolute nor on the site, and texts too long.
    /// </summary>
    public static IReadOnlyList<DefinitionProblem> Check(EmailLayoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new DefinitionProblems();
        if (!DefinitionStructureChecks.IsAlias(input.Alias ?? string.Empty))
        {
            problems.Add(DefinitionErrors.AliasInvalid, "$.alias", "An alias is a lowercase ASCII letter, then up to 63 ASCII letters and digits.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.name", "A layout has a name.");
        }
        else if (input.Name.Length > AliasedLibraryTable<EmailLayoutDto>.MaxNameLength)
        {
            problems.Add(DefinitionErrors.LimitExceeded, "$.name", $"A name holds at most {AliasedLibraryTable<EmailLayoutDto>.MaxNameLength} characters.");
        }

        var design = input.Design;
        foreach (var (scheme, colours) in new[] { ("light", design.Light), ("dark", design.Dark) })
        {
            foreach (var (name, value) in Colours(colours))
            {
                if (value is not null && !HexColour().IsMatch(value))
                {
                    problems.Add(DefinitionErrors.InvalidValue, $"$.design.{scheme}.{name}", "A colour is a hex colour, such as #1f4fbf.");
                }
            }
        }

        if (design.LogoUrl is { } logo && design.LogoMedia is null && !IsLogoAddress(logo))
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.design.logoUrl", $"A logo's address is an absolute http or https address, or a path on the site, of at most {MaxAddressLength} characters.");
        }

        foreach (var (name, texts) in new[] { ("header", design.Header), ("footer", design.Footer), ("logoAlt", design.LogoAlt) })
        {
            foreach (var (culture, text) in texts ?? new Dictionary<string, string>())
            {
                if (text.Length > MaxTextLength)
                {
                    problems.Add(DefinitionErrors.LimitExceeded, $"$.design.{name}['{culture}']", $"A text holds at most {MaxTextLength} characters.");
                }
            }
        }

        return problems.ToReadOnlyList();
    }

    /// <summary>Creates a layout for a user, audited; the problems that refuse it otherwise.</summary>
    public Task<EmailLayoutResult> CreateAsync(Guid key, EmailLayoutInput input, Guid? userKey, CancellationToken cancellationToken = default) =>
        CreateAsync(key, input, userKey, duplicateOf: null, cancellationToken);

    /// <summary>
    /// Changes a layout for a user, audited; no layout and no problems when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile.
    /// </summary>
    public async Task<EmailLayoutResult> UpdateAsync(Guid key, int expectedConcurrencyVersion, EmailLayoutInput input, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Check(input) is { Count: > 0 } problems)
        {
            return new EmailLayoutResult(null, problems);
        }

        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return new EmailLayoutResult(null, []);
        }

        var (design, logo) = await WithLogoAsync(input.Design, cancellationToken);
        if (logo is not null)
        {
            return new EmailLayoutResult(null, [logo]);
        }

        try
        {
            var changed = await repository.UpdateAsync(new EmailLayoutUpdate(row.Id, expectedConcurrencyVersion, input.Alias, input.Name.Trim(), Write(design)), cancellationToken);
            if (changed is not null)
            {
                await AuditAsync(SavedAction, changed, userKey, null);
            }

            return new EmailLayoutResult(changed, []);
        }
        catch (FormBuilderConflictException exception) when (exception.Conflict is FormBuilderConflict.DuplicateAlias)
        {
            return new EmailLayoutResult(null, [Taken()]);
        }
    }

    /// <summary>
    /// Deletes a layout for a user, audited, unless it is the site's default. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile, and with <see cref="FormBuilderConflict.InUse"/>
    /// while an email action names it.
    /// </summary>
    public async Task<EmailLayoutDelete> DeleteAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return EmailLayoutDelete.NotFound;
        }

        if (await IsADefaultAsync(key, cancellationToken))
        {
            return EmailLayoutDelete.IsDefault;
        }

        if (!await repository.DeleteAsync(row.Id, expectedConcurrencyVersion, cancellationToken))
        {
            return EmailLayoutDelete.NotFound;
        }

        await AuditAsync(DeletedAction, row, userKey, null);
        return EmailLayoutDelete.Deleted;
    }

    /// <summary>A copy of a layout with the name given and the first free alias after its own; null when it does not exist.</summary>
    public async Task<EmailLayoutResult?> DuplicateAsync(Guid key, string name, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (await repository.GetAsync(key, cancellationToken) is not { } source)
        {
            return null;
        }

        var input = new EmailLayoutInput(await FreeAliasAsync(source.Alias, cancellationToken), name, DesignOf(source.Layout));
        return await CreateAsync(Guid.NewGuid(), input, userKey, source.Key, cancellationToken);
    }

    /// <summary>
    /// A sample entry's mail in a layout as an editor holds it, saved or not, in a culture: the logo's address read from
    /// its media item, under the base address given. The problems that refuse the layout otherwise.
    /// </summary>
    public async Task<(RenderedEmail? Mail, IReadOnlyList<DefinitionProblem> Problems)> PreviewAsync(EmailLayoutDesign design, string culture, string? baseUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(culture);
        if (Check(new EmailLayoutInput("preview", "preview", design)) is { Count: > 0 } problems)
        {
            return (null, problems);
        }

        var (resolved, logo) = await WithLogoAsync(design, cancellationToken);
        if (logo is not null)
        {
            return (null, [logo]);
        }

        var language = culture.Split('-')[0];
        var sample = Samples.TryGetValue(language, out var said) ? said : Samples["en"];
        var sender = (await settings.GetAsync<EmailSettings>(cancellationToken: cancellationToken)).Value.SenderName;
        return (renderer.Render(new EmailPage(sample.Subject, sample.Body, culture, resolved, sender, baseUrl)), []);
    }

    /// <summary>Sends a test mail in a saved layout to an address, through the outbox, audited.</summary>
    public async Task<TestEmailStatus?> SendTestAsync(Guid key, string? address, string? culture, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return null;
        }

        var status = await tests.SendAsync(address, culture, null, key, cancellationToken);
        if (status == TestEmailStatus.Queued)
        {
            await AuditAsync(TestSentAction, row, userKey, null);
        }

        return status;
    }

    private async Task<EmailLayoutResult> CreateAsync(Guid key, EmailLayoutInput input, Guid? userKey, Guid? duplicateOf, CancellationToken cancellationToken)
    {
        if (Check(input) is { Count: > 0 } problems)
        {
            return new EmailLayoutResult(null, problems);
        }

        var (design, logo) = await WithLogoAsync(input.Design, cancellationToken);
        if (logo is not null)
        {
            return new EmailLayoutResult(null, [logo]);
        }

        try
        {
            var created = await repository.CreateAsync(new NewEmailLayout(key, input.Alias, input.Name.Trim(), Write(design)), cancellationToken);
            await AuditAsync(CreatedAction, created, userKey, duplicateOf);
            return new EmailLayoutResult(created, []);
        }
        catch (FormBuilderConflictException exception) when (exception.Conflict is FormBuilderConflict.DuplicateAlias or FormBuilderConflict.DuplicateKey)
        {
            return new EmailLayoutResult(null, [Taken()]);
        }
    }

    // The design with its logo's address read from its media item; a problem when the item is gone or holds no file.
    private Task<(EmailLayoutDesign Design, DefinitionProblem? Problem)> WithLogoAsync(EmailLayoutDesign design, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (design.LogoMedia is not { } key)
        {
            return Task.FromResult<(EmailLayoutDesign, DefinitionProblem?)>((design, null));
        }

        var item = media.GetById(key);
        var url = item?.GetUrl(global::Umbraco.Cms.Core.Constants.Conventions.Media.File, mediaUrls);
        return Task.FromResult<(EmailLayoutDesign, DefinitionProblem?)>(string.IsNullOrEmpty(url)
            ? (design, new DefinitionProblem(DefinitionErrors.InvalidReference, "$.design.logoMedia", "The logo's media item is gone, or holds no file."))
            : (design with { LogoUrl = url }, null));
    }

    private static string Write(EmailLayoutDesign design) => JsonSerializer.Serialize(design, EmailLayouts.Json);

    private static bool IsLogoAddress(string address) =>
        address.Length <= MaxAddressLength
        && ((address.StartsWith('/') && !address.StartsWith("//", StringComparison.Ordinal))
            || (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"));

    private static IEnumerable<(string Name, string? Value)> Colours(EmailColours? colours)
    {
        if (colours is null)
        {
            yield break;
        }

        yield return ("accent", colours.Accent);
        yield return ("page", colours.Page);
        yield return ("surface", colours.Surface);
        yield return ("text", colours.Text);
        yield return ("muted", colours.Muted);
        yield return ("rule", colours.Rule);
    }

    private static DefinitionProblem Taken() =>
        new(DefinitionErrors.AliasDuplicate, "$.alias", "Another email layout has this alias (letter case does not count).");

    // The layout's alias with the first number after it that no layout has.
    private async Task<string> FreeAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var stem = alias.Length > 60 ? alias[..60] : alias;
        for (var number = 2; ; number++)
        {
            var candidate = stem + number.ToString(CultureInfo.InvariantCulture);
            if (await repository.GetByAliasAsync(candidate, cancellationToken) is null)
            {
                return candidate;
            }
        }
    }

    private Task<StoredAuditEntry> AuditAsync(string action, StoredEmailLayout layout, Guid? userKey, Guid? duplicateOf)
    {
        var details = new AuditDetails().AddIdentifier("alias", layout.Alias);
        if (duplicateOf is { } source)
        {
            details.Add("duplicateOf", source);
        }

        return audit.AddAsync(new NewAuditEntry(action, EntityType, layout.Key, userKey, details), CancellationToken.None);
    }

    // The sample entry the preview shows, in the languages the backoffice speaks; HTML, its texts plain and encoded.
    private static readonly FrozenDictionary<string, (string Subject, string Body)> Samples = new Dictionary<string, (string Subject, string Body)>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ("A new entry from Contact", Sample("Alex Sample sent a message from /contact.", ("Name", "Alex Sample"), ("Email address", "alex@example.com"), ("Your message", "A sample message, as long as a real one usually is."))),
        ["de"] = ("Ein neuer Eintrag aus Kontakt", Sample("Alex Beispiel hat eine Nachricht von /kontakt gesendet.", ("Name", "Alex Beispiel"), ("E-Mail-Adresse", "alex@example.com"), ("Ihre Nachricht", "Eine Beispielnachricht, so lang wie eine echte meist ist."))),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static string Sample(string lead, params (string Label, string Value)[] answers) =>
        $"<p>{WebUtility.HtmlEncode(lead)}</p><table role=\"presentation\">{string.Concat(answers.Select(answer => $"<tr><th scope=\"row\">{WebUtility.HtmlEncode(answer.Label)}</th><td>{WebUtility.HtmlEncode(answer.Value)}</td></tr>"))}</table>";

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexColour();
}
