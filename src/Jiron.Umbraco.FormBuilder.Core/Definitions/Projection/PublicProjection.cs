using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

/// <summary>
/// Builds the public projection of a definition (ADR-0002, ADR-0035) property by property from an allowlist, and its
/// <c>PublicHash</c>: the SHA-256 of its canonical form over all cultures, taken from the stored texts before the
/// sanitiser and the dictionary, so a package update or a dictionary edit never changes it. The public revision moves
/// only when the hash does (step 4.5).
/// </summary>
internal static class PublicProjection
{
    /// <summary>The projection of a definition the save checks accept.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="types">The field types, which say which of their settings visitors see.</param>
    public static PublicForm Create(FormDefinition definition, FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(types);
        var settings = definition.Settings;
        var protection = settings.Protection;
        return new PublicForm
        {
            Key = definition.Key,
            Settings = new PublicFormSettings
            {
                SubmitLabel = settings.Submission.SubmitLabel,
                NextLabel = settings.Submission.NextLabel,
                BackLabel = settings.Submission.BackLabel,
                SummaryPage = settings.Submission.SummaryPage,
                Progress = settings.Submission.Progress,
                Resume = settings.Submission.Resume is not null,
                ClosedMessage = settings.Availability.ClosedMessage,
                MembersOnly = settings.Availability.Access != MemberAccess.Anyone,
                Protection = protection.Level,
                CaptchaProvider = protection.Level == ProtectionLevel.Captcha ? protection.Captcha?.Provider : null,
                Theme = settings.Appearance.Theme,
                RequiredMarker = settings.Appearance.RequiredMarker,
                ValidationSummary = settings.Appearance.ValidationSummary,
                ChoiceLayout = settings.Appearance.ChoiceLayout,
                CssClass = settings.Appearance.CssClass,
                QueryValues = [.. settings.Developer.QueryValues],
            },
            Pages = [.. definition.Pages.Select(page => Page(page, types))],
            ValidationRules = [.. definition.ValidationRules.Where(rule => rule.Enabled).Select(rule => new PublicValidationRule
            {
                Key = rule.Key,
                Field = rule.Field,
                Message = rule.Message,
                Match = rule.Match,
                Conditions = [.. rule.Conditions],
            })],
        };
    }

    /// <summary>The canonical form of a projection as UTF-8 bytes, written as a definition is (ADR-0034).</summary>
    /// <exception cref="ArgumentException">The projection holds what the canonical form cannot carry, such as a lone surrogate.</exception>
    public static byte[] Write(PublicForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        byte[] serialized;
        try
        {
            serialized = JsonSerializer.SerializeToUtf8Bytes(form, PublicJsonContext.Default.PublicForm);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The projection cannot be written: {exception.Message}", nameof(form), exception);
        }

        using var document = JsonDocument.Parse(serialized, new JsonDocumentOptions { MaxDepth = FormDefinitionSerializer.ModelDepth });
        return CanonicalJson.WriteUtf8(document.RootElement, sortProperties: false);
    }

    /// <summary>The <c>PublicHash</c> of a projection: the SHA-256 of its canonical bytes, in lowercase hex.</summary>
    public static string Hash(PublicForm form) => CanonicalJson.Hash(Write(form));

    /// <summary>The <c>PublicHash</c> of a definition's projection.</summary>
    public static string Hash(FormDefinition definition, FieldTypeRegistry types) => Hash(Create(definition, types));

    private static PublicPage Page(FormPage page, FieldTypeRegistry types) => new()
    {
        Key = page.Key,
        Title = page.Title,
        Description = page.Description,
        Rules = Rules(page.Rules),
        Groups = [.. page.Groups.Select(group => new PublicGroup
        {
            Key = group.Key,
            Title = group.Title,
            Description = group.Description,
            Columns = group.Columns,
            Style = group.Style,
            Rules = Rules(group.Rules),
            Fields = [.. group.Fields.Select(field => Field(field, types))],
        })],
    };

    private static PublicField Field(FormField field, FieldTypeRegistry types)
    {
        var traits = types.Find(field.Type);
        return new PublicField
        {
            Key = field.Key,
            Type = field.Type,
            Alias = field.Alias,
            Span = field.Span,
            Label = field.Label,
            HideLabel = field.HideLabel,
            Help = field.Help,
            Placeholder = field.Placeholder,
            Required = field.Required,
            Messages = field.Messages,
            // A type the site lacks may keep its default on the server, so its default stays there too.
            DefaultValue = traits is null or { ServerOnlyDefault: true } ? null : field.DefaultValue,
            Prefill = field.Prefill is { } prefill && (prefill.Query is not null || prefill.Member is not null)
                ? new PublicPrefill { Query = prefill.Query, FromMember = prefill.Member is not null, MemberMode = prefill.MemberMode }
                : null,
            Autocomplete = field.Autocomplete,
            CssClass = field.CssClass,
            Validation = [.. field.Validation],
            Rules = Rules(field.Rules),
            Options = field.Options is { } options ? Options(options) : null,
            Rows = [.. field.Rows.Select(row => new PublicGridRow { Key = row.Key, Label = row.Label, Required = row.Required })],
            Fields = [.. field.Fields.Select(child => Field(child, types))],
            Settings = PublicSettings(field.Settings, traits),
        };
    }

    // Own options without their places; options from a source say only that they come from one.
    private static PublicOptions Options(FieldOptions options) => new()
    {
        Items = options.Source == OptionSource.Items
            ? [.. options.Items.Select(item => new PublicOption
            {
                Value = item.Value,
                Label = item.Label,
                Description = item.Description,
                Image = item.Image,
                ParentValues = [.. item.ParentValues],
            })]
            : [],
        FromSource = options.Source != OptionSource.Items,
        Parent = options.Parent,
    };

    private static List<PublicRule> Rules(IReadOnlyList<FormRule> rules) =>
        [.. rules.Where(rule => rule.Enabled).Select(rule => new PublicRule { Key = rule.Key, Effect = rule.Effect, Match = rule.Match, Conditions = [.. rule.Conditions] })];

    // Only the settings the type makes public; a type the site does not know makes none public.
    private static SettingsObject? PublicSettings(SettingsObject? settings, FieldTypeTraits? traits)
    {
        if (settings is null || traits is null || traits.PublicSettings.Count == 0)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            var kept = false;
            foreach (var property in settings.Value.EnumerateObject())
            {
                if (traits.PublicSettings.Contains(property.Name))
                {
                    property.WriteTo(writer);
                    kept = true;
                }
            }

            writer.WriteEndObject();
            if (!kept)
            {
                return null;
            }
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return SettingsObject.From(document.RootElement);
    }
}
