namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

// The public projection (ADR-0002, ADR-0035): what of a published definition may reach a visitor, as types of its
// own. A property added to the definition stays private until it is added here. Value types that hold only what
// visitors see or what both runtimes evaluate (texts, default values, conditions and their operands, checks and
// messages) are shared with the definition; everything that mixes public and private parts has its public twin here.

/// <summary>
/// The public projection of a form definition: the pages, groups and fields visitors fill in, the rules and checks
/// both runtimes run, and the settings the page needs. It never holds actions, outcomes, recipients, option-source
/// configuration, member prefill mappings, storage, approval, retention or anything a field type keeps private. The
/// form's alias, its public revision and what changes from moment to moment (whether it is open, places left) are
/// added by the endpoint that serves it.
/// </summary>
public sealed record PublicForm
{
    /// <summary>The form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The settings the page needs.</summary>
    public PublicFormSettings Settings { get; init; } = new();

    /// <summary>The pages, in order.</summary>
    public IReadOnlyList<PublicPage> Pages { get; init; } = [];

    /// <summary>The switched-on checks across fields.</summary>
    public IReadOnlyList<PublicValidationRule> ValidationRules { get; init; } = [];
}

/// <summary>The form settings a visitor's page needs.</summary>
public sealed record PublicFormSettings
{
    /// <summary>The submit button's label; absent means the default label.</summary>
    public LocalizedText? SubmitLabel { get; init; }

    /// <summary>The "next page" button's label.</summary>
    public LocalizedText? NextLabel { get; init; }

    /// <summary>The "previous page" button's label.</summary>
    public LocalizedText? BackLabel { get; init; }

    /// <summary>Whether a "check your answers" page comes before sending.</summary>
    public bool SummaryPage { get; init; }

    /// <summary>How a form with several pages shows progress.</summary>
    public ProgressIndicator Progress { get; init; } = ProgressIndicator.Steps;

    /// <summary>Whether the visitor can save their answers and finish later.</summary>
    public bool Resume { get; init; }

    /// <summary>The message a closed form shows.</summary>
    public LocalizedHtml? ClosedMessage { get; init; }

    /// <summary>Whether only signed-in members may fill in the form.</summary>
    public bool MembersOnly { get; init; }

    /// <summary>The protection level; the site's level applies for <see cref="ProtectionLevel.Inherit"/>.</summary>
    public ProtectionLevel Protection { get; init; } = ProtectionLevel.Inherit;

    /// <summary>The alias of the captcha provider, for the captcha level.</summary>
    public string? CaptchaProvider { get; init; }

    /// <summary>The alias of the form's theme; absent means the site's default theme.</summary>
    public string? Theme { get; init; }

    /// <summary>How required and optional fields are marked.</summary>
    public RequiredMarker RequiredMarker { get; init; } = RequiredMarker.Inherit;

    /// <summary>Whether a summary of the errors shows above the form.</summary>
    public bool ValidationSummary { get; init; } = true;

    /// <summary>How choice fields lay out their options unless a field says otherwise.</summary>
    public ChoiceLayout ChoiceLayout { get; init; } = ChoiceLayout.Vertical;

    /// <summary>A CSS class for the form's wrapper.</summary>
    public string? CssClass { get; init; }

    /// <summary>The query values the form reads, for prefill and rules.</summary>
    public IReadOnlyList<string> QueryValues { get; init; } = [];
}

/// <summary>A page of the public projection.</summary>
public sealed record PublicPage
{
    /// <summary>The page's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The page's title.</summary>
    public LocalizedText? Title { get; init; }

    /// <summary>A text above the page's groups.</summary>
    public LocalizedHtml? Description { get; init; }

    /// <summary>The switched-on rules.</summary>
    public IReadOnlyList<PublicRule> Rules { get; init; } = [];

    /// <summary>The page's groups.</summary>
    public IReadOnlyList<PublicGroup> Groups { get; init; } = [];
}

/// <summary>A group of the public projection.</summary>
public sealed record PublicGroup
{
    /// <summary>The group's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The group's title.</summary>
    public LocalizedText? Title { get; init; }

    /// <summary>A text above the group's fields.</summary>
    public LocalizedHtml? Description { get; init; }

    /// <summary>The number of columns, 1 to 4.</summary>
    public int Columns { get; init; } = 1;

    /// <summary>How the group frames its fields.</summary>
    public GroupStyle Style { get; init; } = GroupStyle.Plain;

    /// <summary>The switched-on rules.</summary>
    public IReadOnlyList<PublicRule> Rules { get; init; } = [];

    /// <summary>The group's fields.</summary>
    public IReadOnlyList<PublicField> Fields { get; init; } = [];
}

/// <summary>
/// A field of the public projection: never whether it is sensitive, whether mail lists it, how long it is kept, its
/// former aliases, where a member value comes from, or settings its type keeps private.
/// </summary>
public sealed record PublicField
{
    /// <summary>The field's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The field's type.</summary>
    public required string Type { get; init; }

    /// <summary>The field's alias.</summary>
    public string? Alias { get; init; }

    /// <summary>How many of the group's columns the field spans.</summary>
    public int Span { get; init; } = 1;

    /// <summary>The label.</summary>
    public LocalizedText? Label { get; init; }

    /// <summary>Whether the label is hidden from sight; it stays for screen readers.</summary>
    public bool HideLabel { get; init; }

    /// <summary>A help text below the label.</summary>
    public LocalizedText? Help { get; init; }

    /// <summary>The placeholder inside an empty input.</summary>
    public LocalizedText? Placeholder { get; init; }

    /// <summary>Whether an answer is required.</summary>
    public bool Required { get; init; }

    /// <summary>Custom messages for the type's error kinds.</summary>
    public FieldMessages Messages { get; init; } = new();

    /// <summary>The default value; absent for a type that resolves it on the server.</summary>
    public FieldDefaultValue? DefaultValue { get; init; }

    /// <summary>Where the value is prefilled from.</summary>
    public PublicPrefill? Prefill { get; init; }

    /// <summary>The HTML autocomplete token.</summary>
    public string? Autocomplete { get; init; }

    /// <summary>A CSS class for the field's wrapper.</summary>
    public string? CssClass { get; init; }

    /// <summary>The field's checks.</summary>
    public IReadOnlyList<FieldCheck> Validation { get; init; } = [];

    /// <summary>The switched-on rules.</summary>
    public IReadOnlyList<PublicRule> Rules { get; init; } = [];

    /// <summary>The options of a choice field.</summary>
    public PublicOptions? Options { get; init; }

    /// <summary>The rows of a grid.</summary>
    public IReadOnlyList<PublicGridRow> Rows { get; init; } = [];

    /// <summary>The child fields of a repeatable group.</summary>
    public IReadOnlyList<PublicField> Fields { get; init; } = [];

    /// <summary>The part of the type's settings the type makes public.</summary>
    public SettingsObject? Settings { get; init; }
}

/// <summary>Where a field's value is prefilled from: a query value, or the signed-in member (which property stays on the server).</summary>
public sealed record PublicPrefill
{
    /// <summary>The name of an allowed query value.</summary>
    public string? Query { get; init; }

    /// <summary>Whether the value comes from the signed-in member.</summary>
    public bool FromMember { get; init; }

    /// <summary>Whether a value from the member is shown.</summary>
    public PrefillMemberMode MemberMode { get; init; } = PrefillMemberMode.Show;
}

/// <summary>A choice field's options: its own, or a note that they come from a source when the form is shown.</summary>
public sealed record PublicOptions
{
    /// <summary>The field's own options; empty for options from a source.</summary>
    public IReadOnlyList<PublicOption> Items { get; init; } = [];

    /// <summary>Whether the options come from a library list or another source, resolved when the form is shown.</summary>
    public bool FromSource { get; init; }

    /// <summary>The key of the choice field whose answer filters these options.</summary>
    public Guid? Parent { get; init; }
}

/// <summary>One of a field's own options, without its places: places left change from moment to moment.</summary>
public sealed record PublicOption
{
    /// <summary>The value entries store.</summary>
    public required string Value { get; init; }

    /// <summary>The option's label.</summary>
    public LocalizedText? Label { get; init; }

    /// <summary>A text below the label.</summary>
    public LocalizedText? Description { get; init; }

    /// <summary>The key of a media item shown with the option.</summary>
    public Guid? Image { get; init; }

    /// <summary>The answers of the parent field this option belongs to.</summary>
    public IReadOnlyList<string> ParentValues { get; init; } = [];
}

/// <summary>A row of a grid.</summary>
public sealed record PublicGridRow
{
    /// <summary>The row's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The statement.</summary>
    public LocalizedText? Label { get; init; }

    /// <summary>Whether the row needs an answer when the grid itself is not required.</summary>
    public bool Required { get; init; }
}

/// <summary>A switched-on rule; switched-off rules stay out of the projection.</summary>
public sealed record PublicRule
{
    /// <summary>The rule's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>What the rule does while it matches.</summary>
    public required RuleEffect Effect { get; init; }

    /// <summary>Whether all conditions must match or one is enough.</summary>
    public RuleMatch Match { get; init; } = RuleMatch.All;

    /// <summary>The conditions and nested groups.</summary>
    public IReadOnlyList<RuleItem> Conditions { get; init; } = [];
}

/// <summary>A switched-on check across fields: while it matches, the field shows the message as an error.</summary>
public sealed record PublicValidationRule
{
    /// <summary>The rule's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The key of the field that shows the error.</summary>
    public required Guid Field { get; init; }

    /// <summary>The error message.</summary>
    public LocalizedText? Message { get; init; }

    /// <summary>Whether all conditions must match or one is enough.</summary>
    public RuleMatch Match { get; init; } = RuleMatch.All;

    /// <summary>The conditions and nested groups.</summary>
    public IReadOnlyList<RuleItem> Conditions { get; init; } = [];
}
