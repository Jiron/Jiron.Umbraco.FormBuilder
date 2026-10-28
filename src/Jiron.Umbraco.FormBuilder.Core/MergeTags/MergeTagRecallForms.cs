using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The form answer recall reads in the browser (ADR-0068, ADR-0085), as <c>recallAnswers</c> takes it: the pages, groups
/// and fields that own a visitor text with tags or are recalled, with what holds them, in the form's order; and for the
/// recalled fields, a repeatable group whole with any of them, their aliases and what their answers show as, held to the
/// rules the vectors' runner holds both runtimes to. Every text is the visitor's form's, in the page's culture.
/// </summary>
internal sealed class MergeTagRecallForms(FieldTypeRegistry types, IFormBuilderOptionService options)
{
    // Every country code, in one order, so the browser names an answer as the server does whatever code it holds.
    private static readonly IReadOnlyList<string> Countries = [.. BuiltInChecks.CountryCodes.Order(StringComparer.Ordinal)];

    /// <summary>Writes the recall form as a JSON object.</summary>
    /// <param name="writer">Where it goes.</param>
    /// <param name="shown">The visitor's form, its texts the strings the culture shows.</param>
    /// <param name="owners">The owners of the visitor texts that hold tags.</param>
    /// <param name="recalled">The fields those texts recall.</param>
    /// <param name="scope">The form's definition, the culture and the page, for the sensitive fields and the options of lists.</param>
    /// <param name="cancellationToken">Cancels the option sources' work.</param>
    public async Task WriteAsync(Utf8JsonWriter writer, PublicForm shown, IReadOnlySet<Guid> owners, IReadOnlyCollection<Guid> recalled, MergeTagScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(recalled);
        ArgumentNullException.ThrowIfNull(scope);
        var walk = new Walk(Definitions(scope.Form), owners, Shown(shown, recalled), scope);
        writer.WriteStartObject();
        writer.WriteStartArray("pages");
        foreach (var page in shown.Pages.Where(walk.Needs))
        {
            writer.WriteStartObject();
            writer.WriteString("id", page.Key);
            writer.WriteStartArray("groups");
            foreach (var group in page.Groups.Where(walk.Needs))
            {
                writer.WriteStartObject();
                writer.WriteString("id", group.Key);
                writer.WriteStartArray("fields");
                foreach (var field in group.Fields.Where(walk.Needs))
                {
                    await WriteFieldAsync(writer, walk, field, null, cancellationToken);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    // A field: what its answers show as when it is recalled, otherwise only what places it; its children that are needed.
    private async Task WriteFieldAsync(Utf8JsonWriter writer, Walk walk, PublicField field, FormField? repeater, CancellationToken cancellationToken)
    {
        var traits = types.Find(field.Type);
        var definition = walk.Definitions.GetValueOrDefault(field.Key);
        writer.WriteStartObject();
        writer.WriteString("id", field.Key);
        writer.WriteString("type", field.Type);
        writer.WriteBoolean("holdsValue", traits?.HoldsValue == true);
        if (traits is not null && definition is not null && walk.Recalled.Contains(field.Key))
        {
            writer.WriteBoolean("builtIn", BuiltInFieldTypes.All.Contains(traits.Type.GetType()));
            if (traits.KindOf(definition) is { } kind)
            {
                writer.WriteString("kind", EnumNames<FieldKind>.ByValue[kind]);
            }
            else
            {
                writer.WriteNull("kind");
            }

            writer.WriteString("alias", field.Alias);
            if (field.Label is { } label)
            {
                writer.WriteString("label", label.Default);
            }

            if (traits.Storage == FieldStorageKind.Json)
            {
                writer.WriteBoolean("parts", true);
            }

            if (traits.ServerOnlyDefault)
            {
                writer.WriteString("recall", "none");
            }
            else if (traits.HoldsValue && (definition.Sensitive || repeater?.Sensitive == true))
            {
                writer.WriteString("recall", "masked");
            }

            if (field.Settings is { } settings)
            {
                writer.WritePropertyName("settings");
                settings.Value.WriteTo(writer);
            }

            await WriteOptionsAsync(writer, walk, field, definition, traits, cancellationToken);
            if (field.Rows.Count > 0)
            {
                writer.WriteStartArray("rows");
                foreach (var row in field.Rows)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", row.Key);
                    writer.WriteString("label", row.Label?.Default ?? string.Empty);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }
        }

        var children = field.Fields.Where(walk.Needs).ToList();
        if (children.Count > 0)
        {
            writer.WriteStartArray("fields");
            foreach (var child in children)
            {
                await WriteFieldAsync(writer, walk, child, definition, cancellationToken);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    // The labels its answers show: a country's and an address's countries as the server names any code, its own options,
    // or those its list or source gives for no parent answer; a cascading field's depend on its parent's answers.
    private async Task WriteOptionsAsync(Utf8JsonWriter writer, Walk walk, PublicField field, FormField definition, FieldTypeTraits traits, CancellationToken cancellationToken)
    {
        IEnumerable<(string Value, string Label)> labels;
        if (traits.Type is CountryFieldType or AddressFieldType)
        {
            labels = Countries.Select(code => (code, CountryNames.Name(code, walk.Scope.Culture)));
        }
        else if (field.Options is not { } given)
        {
            return;
        }
        else if (!given.FromSource)
        {
            labels = given.Items.Select(item => (item.Value, item.Label?.Default ?? string.Empty));
        }
        else if (given.Parent is null
            && await options.GetAsync(definition, new FormBuilderOptionContext(walk.Scope.Culture) { Page = walk.Scope.Page }, cancellationToken) is { IsAvailable: true } resolved)
        {
            labels = resolved.Options.Select(option => (option.Value, option.Label ?? string.Empty));
        }
        else
        {
            return;
        }

        writer.WriteStartArray("options");
        foreach (var (value, label) in labels)
        {
            writer.WriteStartObject();
            writer.WriteString("value", value);
            writer.WriteString("label", label);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // The recalled fields, a repeatable group whole with any of them: a tag names its child through it, and its rows show
    // all its children.
    private static HashSet<Guid> Shown(PublicForm shown, IReadOnlyCollection<Guid> recalled)
    {
        var fields = new HashSet<Guid>(recalled);
        foreach (var field in shown.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (fields.Contains(field.Key) || field.Fields.Any(child => fields.Contains(child.Key)))
            {
                fields.Add(field.Key);
                fields.UnionWith(field.Fields.Select(child => child.Key));
            }
        }

        return fields;
    }

    // The definition's fields by key, a repeatable group's children among them.
    private static Dictionary<Guid, FormField> Definitions(FormDefinition form)
    {
        var fields = new Dictionary<Guid, FormField>();
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            fields[field.Key] = field;
            foreach (var child in field.Fields)
            {
                fields[child.Key] = child;
            }
        }

        return fields;
    }

    // What the walk keeps: an element that owns a text with tags, is recalled, or holds one that is.
    private sealed class Walk(Dictionary<Guid, FormField> definitions, IReadOnlySet<Guid> owners, HashSet<Guid> recalled, MergeTagScope scope)
    {
        public Dictionary<Guid, FormField> Definitions { get; } = definitions;

        public HashSet<Guid> Recalled { get; } = recalled;

        public MergeTagScope Scope { get; } = scope;

        public bool Needs(PublicPage page) => owners.Contains(page.Key) || page.Groups.Any(Needs);

        public bool Needs(PublicGroup group) => owners.Contains(group.Key) || group.Fields.Any(Needs);

        public bool Needs(PublicField field) => owners.Contains(field.Key) || Recalled.Contains(field.Key) || field.Fields.Any(Needs);
    }
}
