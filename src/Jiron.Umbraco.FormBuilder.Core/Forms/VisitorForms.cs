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
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A published form as a visitor's page gets it, each part as UTF-8 JSON (ADR-0084, ADR-0085, ADR-0109).</summary>
/// <param name="Form">The visitor's form.</param>
/// <param name="Fields">
/// What the browser decides each field by, by the field's key, its child fields' too: the kind its rules compare it as
/// and a grid row's, whether it can be required, and whether its type is the built-in one of its alias.
/// </param>
/// <param name="Formats">The culture's formats of numbers and dates, which messages and merge tags print with.</param>
/// <param name="Words">The culture's words for yes and no, a site's or the form's own among them.</param>
/// <param name="MergeTags">What the browser renders the visitor texts that hold tags with; JSON <c>null</c> for none.</param>
/// <param name="Consents">Each consent field's wording hash, by the field's key.</param>
/// <param name="Texts">
/// The messages the site or the form rewords in the culture, by their names in the browser's tables, such as
/// <c>minLength.one</c> (ADR-0230); an empty object for none.
/// </param>
internal sealed record VisitorAnswer(byte[] Form, byte[] Fields, byte[] Formats, byte[] Words, byte[] MergeTags, byte[] Consents, byte[] Texts);

/// <summary>A published form as a visitor's page shows it (ADR-0093): the visitor's form, and its parts as JSON.</summary>
/// <param name="Form">The projection with each text as the culture shows it, a consent's text as the wording it shows.</param>
/// <param name="Answer">Its parts as the form endpoint writes them.</param>
/// <param name="Tagged">The visitor texts that hold tags, in the form's order, which the page renders with its answers (ADR-0095).</param>
/// <param name="Consents">Each consent field's wording hash by the field's key, as <see cref="VisitorAnswer.Consents"/> writes them (ADR-0096).</param>
/// <param name="Texts">
/// The visitor texts the form or the site rewords in the culture, by their keys, as they show (ADR-0230): the server renders
/// with them in the package's place.
/// </param>
internal sealed record VisitorPage(PublicForm Form, VisitorAnswer Answer, IReadOnlyList<VisitorText> Tagged, IReadOnlyDictionary<Guid, string> Consents, IReadOnlyDictionary<string, string> Texts);

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

    // The property of a field that holds its default value, as the form endpoint's merge tags name it (ADR-0118).
    private const string DefaultValue = "defaultValue";

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
        var general = (await settings.GetAsync<GeneralSettings>(scope.Site, cancellationToken)).Value;
        var site = WithSite(projection, general);
        var reworded = VisitorWording.ForCulture(projection.Settings.Texts, general.Texts, culture);

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
        foreach (var wording in reworded.Values)
        {
            if (LocalizedString.TryGetDictionaryKey(wording, out var key))
            {
                keys.Add(key);
            }
        }

        var items = keys.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(keys, culture);
        var (shown, hashes) = await WithWordingsAsync(VisitorForm.Map(site, text => Show(text, culture, items), types), scope, cancellationToken);
        var (tags, tagged) = await MergeTagsAsync(shown, scope, cancellationToken);
        var texts = VisitorWording.Shown(reworded.ToDictionary(pair => pair.Key, pair => Show(new LocalizedText(pair.Value), culture, items), StringComparer.Ordinal));
        var answer = new VisitorAnswer(VisitorForm.Write(shown), Fields(shown), Formats(culture), Words(culture, texts), tags, Consents(hashes), Texts(texts));
        return new VisitorPage(shown, answer, tagged, hashes.ToDictionary(pair => pair.Field, pair => pair.Hash), texts);
    }

    /// <summary>
    /// The visitor texts a form or its site rewords in a culture, as they show (ADR-0230): what a page that shows no form,
    /// such as a post's notice, says them with.
    /// </summary>
    /// <param name="projection">The form's public projection.</param>
    /// <param name="culture">The culture.</param>
    /// <param name="site">The site's root, or null for every site's settings.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyDictionary<string, string>> TextsAsync(PublicForm projection, string culture, Guid? site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var general = (await settings.GetAsync<GeneralSettings>(site, cancellationToken)).Value;
        var reworded = VisitorWording.ForCulture(projection.Settings.Texts, general.Texts, culture);
        if (reworded.Count == 0)
        {
            return VisitorWording.None;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var wording in reworded.Values)
        {
            if (LocalizedString.TryGetDictionaryKey(wording, out var key))
            {
                keys.Add(key);
            }
        }

        var items = keys.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(keys, culture);
        return VisitorWording.Shown(reworded.ToDictionary(pair => pair.Key, pair => Show(new LocalizedText(pair.Value), culture, items), StringComparer.Ordinal));
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

    // The visitor texts that hold tags, each with the paths its place refuses, the values of their tags but recall's for
    // this page, and what recall needs; the default values that hold tags, which the page fills in (ADR-0118); and the
    // visitor texts, for the page that renders them itself.
    private async Task<(byte[] Json, IReadOnlyList<VisitorText> Tagged)> MergeTagsAsync(PublicForm shown, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var texts = new List<(VisitorText Text, MergeTagContext Context, IReadOnlyList<string> Refused)>();
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

            texts.Add((text, context, found.Refused));
            foreach (var (path, value) in found.Values)
            {
                values[context].TryAdd(path, value);
            }

            recalled.AddRange(found.Recalled.Where(field => !recalled.Contains(field)));
        }

        // A default value renders in text, each of a list's texts alike; the page gives its query values and its clock.
        var defaults = new List<(Guid Owner, List<string> Refused)>();
        foreach (var field in shown.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
        {
            if (field.DefaultValue is not { } given)
            {
                continue;
            }

            var renders = false;
            var refused = new List<string>();
            foreach (var text in given.Values)
            {
                var found = await mergeTags.ValuesAsync(text, MergeTagPlace.DefaultValue, MergeTagContext.Text, scope with { TextOwner = field.Key }, cancellationToken);
                renders |= found.Renders;
                refused.AddRange(found.Refused.Where(path => !refused.Contains(path)));
                foreach (var (path, value) in found.Values)
                {
                    values[MergeTagContext.Text].TryAdd(path, value);
                }
            }

            if (renders)
            {
                defaults.Add((field.Key, refused));
            }
        }

        var buffer = new ArrayBufferWriter<byte>();
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            if (texts.Count == 0 && defaults.Count == 0)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteStartArray("texts");
                foreach (var (text, context, refused) in texts)
                {
                    WriteText(writer, text.Owner, text.Name, context == MergeTagContext.Html ? "html" : "text", refused);
                }

                foreach (var (owner, refused) in defaults)
                {
                    WriteText(writer, owner, DefaultValue, "text", refused);
                }

                writer.WriteEndArray();
                writer.WriteStartObject("values");
                WriteValues(writer, "html", values[MergeTagContext.Html]);
                WriteValues(writer, "text", values[MergeTagContext.Text]);
                writer.WriteEndObject();
                writer.WritePropertyName("recall");
                await recall.WriteAsync(writer, shown, texts.Select(each => each.Text.Owner).ToHashSet(), recalled, scope, cancellationToken);
                writer.WriteEndObject();
            }
        }

        return (buffer.WrittenSpan.ToArray(), [.. texts.Select(each => each.Text)]);
    }

    // A text the browser renders: its owner, the property it is, its context, and the paths its place refuses, if any.
    private static void WriteText(Utf8JsonWriter writer, Guid owner, string name, string context, IReadOnlyList<string> refused)
    {
        writer.WriteStartObject();
        writer.WriteString("owner", owner);
        writer.WriteString("text", name);
        writer.WriteString("context", context);
        if (refused.Count > 0)
        {
            writer.WriteStartArray("refused");
            foreach (var path in refused)
            {
                writer.WriteStringValue(path);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
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

    // Each field's kinds, whether it can be required and whether its type is the built-in one, as the form engine's
    // model reads them (DefinitionLogic): a field whose type the site lacks has no kind and cannot be required.
    private byte[] Fields(PublicForm shown)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var field in shown.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
            {
                var traits = types.Find(field.Type);
                writer.WriteStartObject(field.Key.ToString("D"));
                if (traits?.KindOf(field.Settings) is { } kind)
                {
                    writer.WriteString("kind", EnumNames<FieldKind>.ByValue[kind]);
                }

                if (traits is not null && traits.Supports(FieldFeatures.Rows))
                {
                    writer.WriteString("rowKind", EnumNames<FieldKind>.ByValue[traits.RowKindOf(field.Settings)]);
                }

                writer.WriteBoolean("canBeRequired", traits?.Supports(FieldFeatures.Required) == true);
                writer.WriteBoolean("builtIn", traits is not null && BuiltInFieldTypes.All.Contains(traits.Type.GetType()));
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    // The culture's formats, which the browser prints numbers and dates in, as the server does.
    private byte[] Formats(string culture)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            MergeTagCultures.Write(writer, cultures.For(culture));
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Words(string culture, IReadOnlyDictionary<string, string> texts)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("yes", VisitorWording.Yes(culture, texts));
            writer.WriteString("no", VisitorWording.No(culture, texts));
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    // The reworded messages by their names in the browser's tables (ADR-0230).
    private static byte[] Texts(IReadOnlyDictionary<string, string> texts)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, wording) in VisitorWording.Messages(texts).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(name, wording);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
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
