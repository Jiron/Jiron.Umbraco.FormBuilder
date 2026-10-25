using System.Text.Json.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>A page of a form: groups of fields, shown one page at a time.</summary>
public sealed record FormPage
{
    /// <summary>The page's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The page's title.</summary>
    public LocalizedText? Title { get; init; }

    /// <summary>A text above the page's groups.</summary>
    public LocalizedHtml? Description { get; init; }

    /// <summary>At most one <c>show</c> or <c>hide</c> rule: a hidden page is skipped.</summary>
    public IReadOnlyList<FormRule> Rules { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormRule>.Empty;

    /// <summary>The page's groups, at least one.</summary>
    public IReadOnlyList<FormGroup> Groups { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormGroup>.Empty;
}

/// <summary>A group of fields laid out in columns.</summary>
public sealed record FormGroup
{
    /// <summary>The group's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The group's title.</summary>
    public LocalizedText? Title { get; init; }

    /// <summary>A text above the group's fields.</summary>
    public LocalizedHtml? Description { get; init; }

    /// <summary>The number of columns, 1 to 4.</summary>
    public int Columns { get; init; } = 1;

    /// <summary>How the group frames its fields.</summary>
    public GroupStyle Style { get; init; } = GroupStyle.Plain;

    /// <summary>At most one <c>show</c> or <c>hide</c> rule.</summary>
    public IReadOnlyList<FormRule> Rules { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormRule>.Empty;

    /// <summary>The group's fields; a new group may have none.</summary>
    public IReadOnlyList<FormField> Fields { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormField>.Empty;
}

/// <summary>
/// A field (ADR-0002): the common properties and the type's own <see cref="Settings"/>. A type declares which common
/// properties it supports; one it does not support keeps its default.
/// </summary>
public sealed record FormField
{
    /// <summary>The field's stable key: entries, rules and actions refer to the field by it.</summary>
    public required Guid Key { get; init; }

    /// <summary>The alias of the field's type in the field type registry, for example <c>email</c>.</summary>
    public required string Type { get; init; }

    /// <summary>
    /// The field's alias for merge tags, exports and the API, for types that hold a value: <c>^[a-z][a-zA-Z0-9]{0,63}$</c>,
    /// unique in the form without regard to case, and not reserved (section 4.3.1).
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>Aliases the field had in published versions; merge tags outside the form still resolve them.</summary>
    public IReadOnlyList<string> FormerAliases { get; init => field = DefinitionList.Of(value); } = DefinitionList<string>.Empty;

    /// <summary>How many of the group's columns the field spans, 1 to the group's columns.</summary>
    public int Span { get; init; } = 1;

    /// <summary>The label; a field that holds a value needs one before it can be published.</summary>
    public LocalizedText? Label { get; init; }

    /// <summary>Whether the label is hidden; it stays for screen readers.</summary>
    public bool HideLabel { get; init; }

    /// <summary>A help text below the label.</summary>
    public LocalizedText? Help { get; init; }

    /// <summary>The placeholder inside an empty input.</summary>
    public LocalizedText? Placeholder { get; init; }

    /// <summary>Whether an answer is required.</summary>
    public bool Required { get; init; }

    /// <summary>Custom messages for the type's error kinds; none by default.</summary>
    public FieldMessages Messages { get; init; } = new();

    /// <summary>The default value; merge tags are allowed (ADR-0014).</summary>
    public FieldDefaultValue? DefaultValue { get; init; }

    /// <summary>Where a value may be prefilled from.</summary>
    public FieldPrefill? Prefill { get; init; }

    /// <summary>Whether the value is masked and needs the sensitive-data permission (section 4.10).</summary>
    public bool Sensitive { get; init; }

    /// <summary>Whether the field appears in the answers that mail lists (ADR-0014).</summary>
    public bool IncludeInEmails { get; init; } = true;

    /// <summary>How long the value is kept (section 4.4).</summary>
    public FieldKeep Keep { get; init; } = FieldKeep.Always;

    /// <summary>The HTML autocomplete token, for example <c>email</c>.</summary>
    public string? Autocomplete { get; init; }

    /// <summary>A CSS class for the field's wrapper.</summary>
    public string? CssClass { get; init; }

    /// <summary>The checks no type owns: a pattern, matching another field, unique per form.</summary>
    public IReadOnlyList<FieldCheck> Validation { get; init => field = DefinitionList.Of(value); } = DefinitionList<FieldCheck>.Empty;

    /// <summary>At most one <c>show</c> or <c>hide</c> rule and at most one <c>require</c> rule.</summary>
    public IReadOnlyList<FormRule> Rules { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormRule>.Empty;

    /// <summary>The options of a choice type.</summary>
    public FieldOptions? Options { get; init; }

    /// <summary>The rows of a grid.</summary>
    public IReadOnlyList<GridRow> Rows { get; init => field = DefinitionList.Of(value); } = DefinitionList<GridRow>.Empty;

    /// <summary>The child fields of a repeatable group; no repeatable group among them.</summary>
    public IReadOnlyList<FormField> Fields { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormField>.Empty;

    /// <summary>The type's own settings, validated and read by the type.</summary>
    public SettingsObject? Settings { get; init; }
}

/// <summary>Custom texts for a field's error kinds (section 4.3.3); a missing one uses the type's text.</summary>
public sealed record FieldMessages
{
    /// <summary>No answer was given.</summary>
    public LocalizedText? Required { get; init; }

    /// <summary>The type cannot read the answer.</summary>
    public LocalizedText? Invalid { get; init; }

    /// <summary>The answer is too short.</summary>
    public LocalizedText? MinLength { get; init; }

    /// <summary>The answer is too long.</summary>
    public LocalizedText? MaxLength { get; init; }

    /// <summary>The answer has too few words.</summary>
    public LocalizedText? MinWords { get; init; }

    /// <summary>The answer has too many words.</summary>
    public LocalizedText? MaxWords { get; init; }

    /// <summary>The number is below the minimum.</summary>
    public LocalizedText? Min { get; init; }

    /// <summary>The number is above the maximum.</summary>
    public LocalizedText? Max { get; init; }

    /// <summary>The number is not on a step.</summary>
    public LocalizedText? Step { get; init; }

    /// <summary>The date is outside the allowed window.</summary>
    public LocalizedText? DateWindow { get; init; }

    /// <summary>Too few or too many options are selected.</summary>
    public LocalizedText? Selections { get; init; }

    /// <summary>A file's type is not allowed.</summary>
    public LocalizedText? FileType { get; init; }

    /// <summary>A file is too large.</summary>
    public LocalizedText? FileSize { get; init; }

    /// <summary>Too many files.</summary>
    public LocalizedText? FileCount { get; init; }

    /// <summary>A chosen option has no places left.</summary>
    public LocalizedText? Full { get; init; }
}

/// <summary>Where a field's value may be prefilled from.</summary>
public sealed record FieldPrefill
{
    /// <summary>The name of a query value the form allows (section 4.3.4).</summary>
    public string? Query { get; init; }

    /// <summary>The alias of a member property, read on the server only.</summary>
    public string? Member { get; init; }

    /// <summary>Whether a value from the member is shown.</summary>
    public PrefillMemberMode MemberMode { get; init; } = PrefillMemberMode.Show;
}

/// <summary>The options of a choice field (ADR-0002).</summary>
public sealed record FieldOptions
{
    /// <summary>Where the options come from.</summary>
    public OptionSource Source { get; init; } = OptionSource.Items;

    /// <summary>The field's own options, for <see cref="OptionSource.Items"/>.</summary>
    public IReadOnlyList<FieldOption> Items { get; init => field = DefinitionList.Of(value); } = DefinitionList<FieldOption>.Empty;

    /// <summary>The key of the library option list, for <see cref="OptionSource.List"/>.</summary>
    [JsonPropertyName("list")]
    public Guid? OptionList { get; init; }

    /// <summary>The source's configuration, for the other sources; the source validates it.</summary>
    public SettingsObject? Config { get; init; }

    /// <summary>The key of the choice field whose answer filters these options (cascading).</summary>
    public Guid? Parent { get; init; }
}

/// <summary>One of a choice field's own options.</summary>
public sealed record FieldOption
{
    /// <summary>What entries store: 1 to 200 characters, unique in the field once folded as rules compare text.</summary>
    public required string Value { get; init; }

    /// <summary>The option's label.</summary>
    public LocalizedText? Label { get; init; }

    /// <summary>A text below the label.</summary>
    public LocalizedText? Description { get; init; }

    /// <summary>The key of a media item shown with the option.</summary>
    public Guid? Image { get; init; }

    /// <summary>The number of places, for options that fill up (section 4.4).</summary>
    public int? Places { get; init; }

    /// <summary>The answers of the parent field this option belongs to.</summary>
    public IReadOnlyList<string> ParentValues { get; init => field = DefinitionList.Of(value); } = DefinitionList<string>.Empty;
}

/// <summary>A row of a grid: a statement rated on the grid's columns, stored like a child field.</summary>
public sealed record GridRow
{
    /// <summary>The row's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The statement.</summary>
    public LocalizedText? Label { get; init; }

    /// <summary>Whether the row needs an answer when the grid itself is not required.</summary>
    public bool Required { get; init; }
}

/// <summary>A check no field type owns (ADR-0002), with its own message.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PatternCheck), "pattern")]
[JsonDerivedType(typeof(MatchesFieldCheck), "matchesField")]
[JsonDerivedType(typeof(UniquePerFormCheck), "uniquePerForm")]
public abstract record FieldCheck
{
    private protected FieldCheck()
    {
    }

    /// <summary>The message shown when the check fails.</summary>
    [JsonPropertyOrder(1)]
    public LocalizedText? Message { get; init; }
}

/// <summary>The answer matches a pattern, run on Jiron Form Builder's own matcher (ADR-0005).</summary>
public sealed record PatternCheck : FieldCheck
{
    /// <summary>The pattern, in the safe subset; it matches the whole answer.</summary>
    public required string Pattern { get; init; }
}

/// <summary>The answer equals another field's answer.</summary>
public sealed record MatchesFieldCheck : FieldCheck
{
    /// <summary>The other field's key.</summary>
    public required Guid Field { get; init; }
}

/// <summary>No other entry of the form has the same answer.</summary>
public sealed record UniquePerFormCheck : FieldCheck;
