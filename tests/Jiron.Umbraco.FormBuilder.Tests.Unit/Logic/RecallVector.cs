using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a vector's <c>recall</c> through the merge-tag service in the visitor-text place (ADR-0068): the form and the
/// answers of the file's defaults, the page, group or field whose text it is, and the fields the rules hid. First it
/// holds the form to what the browser's runtime is given: each field's kind, parts, rows and recall mode are its type's,
/// <c>builtIn</c> says whether its type is one of Jiron Form Builder's own, the countries a country or an address lists
/// are named as the server names them, and the words for yes and no are the server's, all in the case's culture.
/// </summary>
internal static class RecallVector
{
    // A site's own types, whose displays the visitor texts do not use: they show a choice by its option's label, a number
    // in the culture's format and parts apart by commas.
    private static readonly Lazy<FieldTypeRegistry> Types = new(() => TestFieldTypes.Build(SiteTypes));

    // The same site with its own yes/no type in place of the built-in one, for a form whose yes/no field is not built in.
    private static readonly Lazy<FieldTypeRegistry> Replaced = new(() =>
        TestFieldTypes.Build(builder => SiteTypes(builder.Replace<YesNoFieldType, SiteYesNoFieldType>())));

    /// <summary>The rendered text as both runners write it: <c>{ "text": "...", "problems": [{ "code", "at" }] }</c>.</summary>
    public static async Task<JsonObject> RunAsync(string id, JsonElement root, JsonElement recall, string culture)
    {
        var defaults = root.GetProperty("defaults");
        var words = defaults.GetProperty("words").TryGetProperty(culture, out var said) ? said : throw new AssertionException($"{id}: the file has no words for {culture}.");
        if (words.GetProperty("yes").GetString() != FieldDisplayWords.Yes(culture) || words.GetProperty("no").GetString() != FieldDisplayWords.No(culture))
        {
            throw new AssertionException($"{id}: the words for yes and no in {culture} are not the server's.");
        }

        var given = defaults.GetProperty("forms").GetProperty(recall.GetProperty("form").GetString()!);
        var types = AllFields(given).Any(field => field.GetProperty("type").GetString() == "yesno" && !field.GetProperty("builtIn").GetBoolean())
            ? Replaced.Value
            : Types.Value;
        var form = Form(id, given, types, culture);
        var answers = Answers(defaults.GetProperty("answers").GetProperty(recall.GetProperty("answers").GetString()!), recall.GetProperty("hidden"));
        var owner = recall.GetProperty("owner") is { ValueKind: JsonValueKind.String } named ? Key(named) : (Guid?)null;
        var cultures = new MergeTagCultures(defaults.GetProperty("cultures").EnumerateObject().Select(each => KeyValuePair.Create(each.Name, VectorReader.Formats(each.Value))));
        var site = MergeTagTestSite.Create(types: types, cultures: cultures);
        var scope = MergeTagTestSite.Scope(form, culture: culture, answers: answers) with { TextOwner = owner };

        var rendered = await site.RenderAsync(
            recall.GetProperty("text").GetString()!,
            scope,
            MergeTagPlace.VisitorText,
            VectorReader.TemplateContextOf(recall.GetProperty("context")));

        return new JsonObject
        {
            ["text"] = rendered.Text,
            ["problems"] = new JsonArray([.. rendered.Problems.Select(problem => (JsonNode)new JsonObject { ["code"] = problem.Code, ["at"] = problem.At })]),
        };
    }

    private static FormDefinition Form(string id, JsonElement form, FieldTypeRegistry types, string culture) => new()
    {
        Key = Guid.Parse("f0000000-0000-4000-8000-000000000068", CultureInfo.InvariantCulture),
        Pages = [.. form.GetProperty("pages").EnumerateArray().Select(page => new FormPage
        {
            Key = Key(page.GetProperty("id")),
            Groups = page.TryGetProperty("groups", out var groups)
                ? [.. groups.EnumerateArray().Select(group => new FormGroup { Key = Key(group.GetProperty("id")), Fields = Fields(id, group, types, culture) })]
                : [],
        })],
    };

    // Every field of a form the vectors write, a repeatable group's children after it.
    private static IEnumerable<JsonElement> AllFields(JsonElement form)
    {
        static IEnumerable<JsonElement> Of(JsonElement holder) =>
            holder.TryGetProperty("fields", out var fields) ? fields.EnumerateArray().SelectMany(field => Of(field).Prepend(field)) : [];

        return form.GetProperty("pages").EnumerateArray()
            .SelectMany(page => page.TryGetProperty("groups", out var groups) ? groups.EnumerateArray() : Enumerable.Empty<JsonElement>())
            .SelectMany(Of);
    }

    private static List<FormField> Fields(string id, JsonElement holder, FieldTypeRegistry types, string culture) =>
        holder.TryGetProperty("fields", out var fields) ? [.. fields.EnumerateArray().Select(field => Field(id, field, types, culture))] : [];

    private static FormField Field(string id, JsonElement field, FieldTypeRegistry types, string culture)
    {
        var alias = field.GetProperty("type").GetString()!;
        var type = types.FindType(alias) ?? throw new AssertionException($"{id}: the site has no type '{alias}'.");
        var traits = types.Find(alias)!;
        var name = field.TryGetProperty("alias", out var given) && given.ValueKind == JsonValueKind.String ? given.GetString() : null;
        var hasOptions = field.TryGetProperty("options", out var options);
        var hasRows = field.TryGetProperty("rows", out var rows);
        var definition = new FormField
        {
            Key = Key(field.GetProperty("id")),
            Type = alias,
            Alias = name,
            Label = field.TryGetProperty("label", out var label) ? new LocalizedText(label.GetString()!) : null,
            Sensitive = Recall(field) == "masked",
            FormerAliases = field.TryGetProperty("formerAliases", out var former) ? VectorReader.Strings(former) : [],
            Settings = field.TryGetProperty("settings", out var settings) ? SettingsObject.Parse(settings.GetRawText()) : null,
            Options = hasOptions && traits.Supports(FieldFeatures.Options)
                ? new FieldOptions { Items = [.. options.EnumerateArray().Select(option => new FieldOption { Value = Text(option, "value"), Label = new LocalizedText(Text(option, "label")) })] }
                : null,
            Rows = hasRows ? [.. rows.EnumerateArray().Select(row => new GridRow { Key = Key(row.GetProperty("id")), Label = new LocalizedText(Text(row, "label")) })] : [],
            Fields = Fields(id, field, types, culture),
        };

        var at = $"{id}: the field '{name ?? alias}'";
        Hold(field.GetProperty("holdsValue").GetBoolean() == traits.HoldsValue, $"{at} holds a value only when its type does.");
        Hold(field.GetProperty("builtIn").GetBoolean() == BuiltInFieldTypes.All.Contains(type.GetType()), $"{at} is built in only when its type is one of the built-in types.");
        Hold((field.TryGetProperty("parts", out var parts) && parts.GetBoolean()) == (traits.Storage == FieldStorageKind.Json), $"{at} has parts only when its type stores them.");
        Hold(Kind(field) == traits.KindOf(definition), $"{at} has its type's kind, {traits.KindOf(definition)?.ToString() ?? "none"}.");
        Hold((Recall(field) == "none") == traits.ServerOnlyDefault, $"{at} shows nothing only when its type's default stays on the server.");
        Hold(hasRows == traits.Supports(FieldFeatures.Rows), $"{at} has rows only when its type has them, and then writes them.");
        Hold(Recall(field) != "masked" || traits.HoldsValue, $"{at} is masked only when it holds a value.");
        if (hasOptions && !traits.Supports(FieldFeatures.Options))
        {
            // A country or an address names its countries as the server names them in the culture.
            Hold(
                options.EnumerateArray().All(option => Text(option, "label") == CountryNames.Name(Text(option, "value"), culture)),
                $"{at} lists countries the server names otherwise in {culture}.");
        }

        return definition;
    }

    private static MergeTagAnswers Answers(JsonElement given, JsonElement hidden)
    {
        var answers = new List<MergeTagAnswer>();
        if (given.TryGetProperty("fields", out var fields))
        {
            answers.AddRange(fields.EnumerateObject().Select(field => Answer(Key(field.Name), field.Value, 0)));
        }

        if (given.TryGetProperty("rows", out var repeaters))
        {
            foreach (var repeater in repeaters.EnumerateObject())
            {
                var rows = repeater.Value.EnumerateArray().ToList();
                for (var row = 0; row < rows.Count; row++)
                {
                    answers.AddRange(rows[row].EnumerateObject().Select(child => Answer(Key(child.Name), child.Value, row)));
                }
            }
        }

        return new MergeTagAnswers(answers, [.. hidden.EnumerateArray().Select(Key)]);
    }

    private static MergeTagAnswer Answer(Guid field, JsonElement answer, int row) => new(
        field,
        [.. answer.GetProperty("values").EnumerateArray().Select(value => new FieldValue(
            Text(value, "text"),
            value.TryGetProperty("row", out var grid) ? Key(grid) : null))],
        answer.TryGetProperty("files", out var files)
            ? [.. files.EnumerateArray().Select(file => new FieldFile(Text(file, "name"), file.GetProperty("size").GetInt64()))]
            : null,
        row);

    private static FieldKind? Kind(JsonElement field) =>
        field.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String ? VectorReader.Kind(kind) : null;

    private static string? Recall(JsonElement field) => field.TryGetProperty("recall", out var recall) ? recall.GetString() : null;

    private static Guid Key(JsonElement element) => Key(element.GetString()!);

    private static Guid Key(string text) => Guid.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static void SiteTypes(FormBuilderFieldTypeCollectionBuilder builder) =>
        builder.Append<StampFieldType>().Append<MeasureFieldType>().Append<BadgeFieldType>();

    private static void Hold(bool holds, string message)
    {
        if (!holds)
        {
            throw new AssertionException(message);
        }
    }

    /// <summary>A site's own type, named <c>stamp</c>, with radio buttons' declaration, which writes its own display.</summary>
    private sealed class StampFieldType : FormBuilderFieldTypeBase<RadioFieldSettings>
    {
        public StampFieldType()
            : base(new RadioFieldType().Declaration with { Alias = "stamp", Name = "Stamp" })
        {
        }

        protected override void Display(RadioFieldSettings settings, FieldDisplay display)
        {
            foreach (var value in display.Values)
            {
                display.Add("STAMP " + value.Text);
            }
        }
    }

    /// <summary>A site's own number type, named <c>measure</c>, which writes its own display.</summary>
    private sealed class MeasureFieldType : FormBuilderFieldTypeBase<NumberFieldSettings>
    {
        public MeasureFieldType()
            : base(new NumberFieldType().Declaration with { Alias = "measure", Name = "Measure" })
        {
        }

        protected override void Display(NumberFieldSettings settings, FieldDisplay display)
        {
            foreach (var value in display.Values)
            {
                display.Add("MEASURE " + value.Text);
            }
        }
    }

    /// <summary>A site's own type with parts, named <c>badge</c>, with a name's declaration, which writes its own display.</summary>
    private sealed class BadgeFieldType : FormBuilderFieldTypeBase<NameFieldSettings>
    {
        public BadgeFieldType()
            : base(new NameFieldType().Declaration with { Alias = "badge", Name = "Badge" })
        {
        }

        protected override void Display(NameFieldSettings settings, FieldDisplay display)
        {
            foreach (var value in display.Values)
            {
                display.Add("BADGE " + value.Text);
            }
        }
    }

    /// <summary>A site's own yes/no type in place of the built-in one, which writes its own display.</summary>
    private sealed class SiteYesNoFieldType : FormBuilderFieldTypeBase<YesNoFieldSettings>
    {
        public SiteYesNoFieldType()
            : base(new YesNoFieldType().Declaration)
        {
        }

        protected override void Display(YesNoFieldSettings settings, FieldDisplay display)
        {
            foreach (var value in display.Values)
            {
                display.Add("SITE " + value.Text);
            }
        }
    }
}
