using System.Buffers;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A published form as a visitor's page gets it, each part as UTF-8 JSON (ADR-0084, ADR-0085).</summary>
/// <param name="Form">The visitor's form.</param>
/// <param name="MergeTags">What the browser renders the visitor texts that hold tags with; JSON <c>null</c> for none.</param>
/// <param name="Consents">Each consent field's wording hash, by the field's key.</param>
internal sealed record VisitorAnswer(byte[] Form, byte[] MergeTags, byte[] Consents);

/// <summary>A published form as a visitor's page shows it (ADR-0093): the visitor's form, and its parts as JSON.</summary>
/// <param name="Form">The projection with each text as the culture shows it, a consent's text as the wording it shows.</param>
/// <param name="Answer">Its parts as the form endpoint writes them.</param>
/// <param name="Tagged">The visitor texts that hold tags, in the form's order, which the page renders with its answers (ADR-0095).</param>
/// <param name="Consents">Each consent field's wording hash by the field's key, as <see cref="VisitorAnswer.Consents"/> writes them (ADR-0096).</param>
internal sealed record VisitorPage(PublicForm Form, VisitorAnswer Answer, IReadOnlyList<VisitorText> Tagged, IReadOnlyDictionary<Guid, string> Consents);

/// <summary>
/// A published form as a visitor's page gets it in one culture (ADR-0084, ADR-0085): the site's required-marker style
/// for a form whose marker is <c>inherit</c>, and the site's labels for the buttons the form labels not itself
/// (ADR-0095); each text of its projection in the culture with the usual fallback (ADR-0002), a text that names a
/// dictionary item (<c>#key</c>) as the item's text in the culture through Umbraco's dictionary, every rich text through
/// the one sanitiser (section 4.10), and a lone surrogate a text may hold replaced, so the form always writes; each
/// consent's text as the wording it shows, with its hash; and for the visitor texts that hold tags, what the browser
/// renders them with.
/// </summary>
internal sealed class VisitorForms(
    MergeTagDictionary dictionary,
    EditorHtml html,
    FieldTypeRegistry types,
    IFormBuilderMergeTagService mergeTags,
    MergeTagRecallForms recall,
    MergeTagCultures cultures,
    ConsentWordings consents,
    IFormBuilderSettingsService settings)
{
    // The consent type's setting that holds its wording.
    private const string ConsentText = "text";

    /// <summary>The visitor's form, its visitor texts and its consents, as a page shows them.</summary>
    /// <param name="projection">The form's public projection.</param>
    /// <param name="scope">
    /// The page: the form's definition (the version the projection is of), name and alias, the culture (an ISO code of
    /// one of the site's languages), the site and the content page.
    /// </param>
    /// <param name="cancellationToken">Cancels the merge-tag and option sources' work.</param>
    public async Task<VisitorAnswer> WriteAsync(PublicForm projection, MergeTagScope scope, CancellationToken cancellationToken = default) =>
        (await ShowAsync(projection, scope, cancellationToken)).Answer;

    /// <summary>The visitor's form as a page shows it, with its parts as JSON, which a Razor page renders (ADR-0093).</summary>
    /// <param name="projection">The form's public projection.</param>
    /// <param name="scope">The page, as for <see cref="WriteAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the merge-tag and option sources' work.</param>
    public async Task<VisitorPage> ShowAsync(PublicForm projection, MergeTagScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(scope);
        var culture = scope.Culture;
        var site = WithSite(projection, (await settings.GetAsync<GeneralSettings>(scope.Site, cancellationToken)).Value);

        // The dictionary items the texts name in this culture, read at once.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitorForm.Map(
            site,
            text =>
            {
                if (LocalizedString.TryGetDictionaryKey(text.Resolve(culture), out var key))
                {
                    keys.Add(key);
                }

                return string.Empty;
            },
            types);
        var items = keys.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(keys, culture);
        var (shown, hashes) = await WithWordingsAsync(VisitorForm.Map(site, text => Show(text, culture, items), types), scope, cancellationToken);
        var (tags, tagged) = await MergeTagsAsync(shown, scope, cancellationToken);
        return new VisitorPage(shown, new VisitorAnswer(VisitorForm.Write(shown), tags, Consents(hashes)), tagged, hashes.ToDictionary(pair => pair.Field, pair => pair.Hash));
    }

    // The site's style for a form whose marker is inherit, the site's label for each button the form labels not itself
    // (ADR-0095), and the site's theme for a form that names none (ADR-0105); a site's label that is no localised text is
    // none, so the page shows the package's word.
    private static PublicForm WithSite(PublicForm projection, GeneralSettings general)
    {
        var own = projection.Settings;
        return projection with
        {
            Settings = own with
            {
                RequiredMarker = own.RequiredMarker == RequiredMarker.Inherit ? general.RequiredMarker : own.RequiredMarker,
                SubmitLabel = own.SubmitLabel ?? Label(general.SubmitLabel),
                NextLabel = own.NextLabel ?? Label(general.NextLabel),
                BackLabel = own.BackLabel ?? Label(general.BackLabel),
                Theme = own.Theme ?? general.Theme,
            },
        };
    }

    private static LocalizedText? Label(IReadOnlyDictionary<string, string>? texts)
    {
        if (texts is not { Count: > 0 })
        {
            return null;
        }

        try
        {
            return new LocalizedText(texts);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string Show(LocalizedString text, string culture, DictionaryTexts items)
    {
        var shown = StrictStringConverter.WellFormed(items.Resolve(text, culture));
        return text is LocalizedHtml ? html.Sanitise(shown) : shown;
    }

    // Each consent's text becomes the wording it shows, which the server renders whole (ADR-0070), and its hash is kept;
    // a wording that renders as nothing shows nothing and has no hash.
    private async Task<(PublicForm Form, List<(Guid Field, string Hash)> Hashes)> WithWordingsAsync(PublicForm shown, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var definitions = scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
            .SelectMany(field => field.Fields.Prepend(field))
            .ToDictionary(field => field.Key);
        var wordings = new Dictionary<Guid, ConsentWording?>();
        foreach (var field in shown.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
        {
            if (types.FindType(field.Type) is ConsentFieldType && definitions.TryGetValue(field.Key, out var definition))
            {
                wordings[field.Key] = await consents.ShownAsync(definition, scope, cancellationToken);
            }
        }

        if (wordings.Count == 0)
        {
            return (shown, []);
        }

        var form = shown with
        {
            Pages = [.. shown.Pages.Select(page => page with
            {
                Groups = [.. page.Groups.Select(group => group with { Fields = [.. group.Fields.Select(field => Worded(field, wordings))] })],
            })],
        };
        return (form, [.. wordings.Where(pair => pair.Value is not null).Select(pair => (pair.Key, pair.Value!.Hash))]);
    }

    private static PublicField Worded(PublicField field, Dictionary<Guid, ConsentWording?> wordings) => field with
    {
        Settings = wordings.TryGetValue(field.Key, out var wording) ? WithText(field.Settings, wording?.Text ?? string.Empty) : field.Settings,
        Fields = [.. field.Fields.Select(child => Worded(child, wordings))],
    };

    // The settings with their text replaced, everything else as it was.
    private static SettingsObject WithText(SettingsObject? settings, string text)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (settings is not null)
            {
                foreach (var property in settings.Value.EnumerateObject().Where(property => property.Name != ConsentText))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteString(ConsentText, text);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return SettingsObject.From(document.RootElement);
    }

    // The visitor texts that hold tags, the values of their tags but recall's for this page, and what recall needs; and
    // those texts, for the page that renders them itself.
    private async Task<(byte[] Json, IReadOnlyList<VisitorText> Tagged)> MergeTagsAsync(PublicForm shown, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var texts = new List<(VisitorText Text, MergeTagContext Context)>();
        var values = new Dictionary<MergeTagContext, Dictionary<string, MergeTagValue>>
        {
            [MergeTagContext.Html] = new(StringComparer.Ordinal),
            [MergeTagContext.Text] = new(StringComparer.Ordinal),
        };
        var recalled = new List<Guid>();
        foreach (var text in VisitorTexts.Of(shown, types))
        {
            var context = text.IsHtml ? MergeTagContext.Html : MergeTagContext.Text;
            var found = await mergeTags.ValuesAsync(text.Text, MergeTagPlace.VisitorText, context, scope with { TextOwner = text.Owner }, cancellationToken);
            if (!found.Renders)
            {
                continue;
            }

            texts.Add((text, context));
            foreach (var (path, value) in found.Values)
            {
                values[context].TryAdd(path, value);
            }

            recalled.AddRange(found.Recalled.Where(field => !recalled.Contains(field)));
        }

        var buffer = new ArrayBufferWriter<byte>();
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            if (texts.Count == 0)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteStartArray("texts");
                foreach (var (text, context) in texts)
                {
                    writer.WriteStartObject();
                    writer.WriteString("owner", text.Owner);
                    writer.WriteString("text", text.Name);
                    writer.WriteString("context", context == MergeTagContext.Html ? "html" : "text");
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteStartObject("values");
                WriteValues(writer, "html", values[MergeTagContext.Html]);
                WriteValues(writer, "text", values[MergeTagContext.Text]);
                writer.WriteEndObject();
                writer.WritePropertyName("recall");
                await recall.WriteAsync(writer, shown, texts.Select(each => each.Text.Owner).ToHashSet(), recalled, scope, cancellationToken);
                writer.WriteStartObject("words");
                writer.WriteString("yes", FieldDisplayWords.Yes(scope.Culture));
                writer.WriteString("no", FieldDisplayWords.No(scope.Culture));
                writer.WriteEndObject();
                writer.WritePropertyName("culture");
                MergeTagCultures.Write(writer, cultures.For(scope.Culture));
                writer.WriteEndObject();
            }
        }

        return (buffer.WrittenSpan.ToArray(), [.. texts.Select(each => each.Text)]);
    }

    // A source's value as the vectors write one: a string, an object with its text and the number or date behind it, or
    // a list of these.
    private static void WriteValues(Utf8JsonWriter writer, string name, Dictionary<string, MergeTagValue> values)
    {
        writer.WriteStartObject(name);
        foreach (var (path, value) in values)
        {
            writer.WritePropertyName(path);
            if (value.IsList)
            {
                writer.WriteStartArray();
                foreach (var item in value.Items)
                {
                    WriteItem(writer, item);
                }

                writer.WriteEndArray();
            }
            else
            {
                WriteItem(writer, value.Items.Count > 0 ? value.Items[0] : new MergeTagItem(string.Empty));
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteItem(Utf8JsonWriter writer, MergeTagItem item)
    {
        if (item.Number is null && item.Date is null)
        {
            writer.WriteStringValue(StrictStringConverter.WellFormed(item.Text));
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("text", StrictStringConverter.WellFormed(item.Text));
        if (item.Number is { } number)
        {
            writer.WriteString("number", number);
        }

        if (item.Date is { } date)
        {
            writer.WriteString("date", date);
        }

        writer.WriteEndObject();
    }

    private static byte[] Consents(List<(Guid Field, string Hash)> hashes)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (field, hash) in hashes)
            {
                writer.WriteString(field.ToString("D"), hash);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
