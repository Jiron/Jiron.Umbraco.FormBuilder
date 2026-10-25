namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// A form definition (section 4.3, ADR-0002): one JSON document per version. The form's name, alias and folder live on
/// the form's row, not here, so restoring a version never renames a form.
/// </summary>
public sealed record FormDefinition
{
    /// <summary>The schema version this model reads and writes; older documents are upgraded when read.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The document's schema version, always <see cref="CurrentSchemaVersion"/> in memory.</summary>
    public int SchemaVersion { get; } = CurrentSchemaVersion;

    /// <summary>The form's key, equal to the key of its row.</summary>
    public required Guid Key { get; init; }

    /// <summary>The form settings (section 4.3.4); a missing group takes its defaults.</summary>
    public FormSettings Settings { get; init; } = new();

    /// <summary>The pages, at least one.</summary>
    public IReadOnlyList<FormPage> Pages { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormPage>.Empty;

    /// <summary>Checks across fields.</summary>
    public IReadOnlyList<FormValidationRule> ValidationRules { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormValidationRule>.Empty;

    /// <summary>What runs after a submission, in run order.</summary>
    public IReadOnlyList<FormAction> Actions { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormAction>.Empty;

    /// <summary>What the visitor gets after sending, in order: the first whose rule matches is used.</summary>
    public IReadOnlyList<FormOutcome> Outcomes { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormOutcome>.Empty;
}

/// <summary>An action: what runs after a submission (section 4.4).</summary>
public sealed record FormAction
{
    /// <summary>The action's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The alias of the action's type, for example <c>email</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The name editors see in lists and logs, for example "Notify sales".</summary>
    public string? Name { get; init; }

    /// <summary>Whether the action runs.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>What starts the action.</summary>
    public ActionTrigger Trigger { get; init; } = ActionTrigger.Submitted;

    /// <summary>Whether a failure stops the actions after this one.</summary>
    public bool StopOnFailure { get; init; }

    /// <summary>At most one <c>run</c> rule: the action runs only while it matches.</summary>
    public IReadOnlyList<FormRule> Rules { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormRule>.Empty;

    /// <summary>
    /// The type's settings. Secrets appear only as <c>$JironFormBuilder:Secrets:&lt;Name&gt;</c> references (section 4.10).
    /// </summary>
    public SettingsObject? Settings { get; init; }
}

/// <summary>What the visitor gets after sending: a message, a redirect, or a message and then a redirect.</summary>
public sealed record FormOutcome
{
    /// <summary>The outcome's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>A message (perhaps followed by a redirect) or a redirect.</summary>
    public OutcomeKind Kind { get; init; } = OutcomeKind.Message;

    /// <summary>The message; merge tags are allowed (ADR-0014).</summary>
    public LocalizedHtml? Message { get; init; }

    /// <summary>Whether the message shows a copy of the answers.</summary>
    public bool ShowAnswers { get; init; }

    /// <summary>Where the visitor goes, at once or after the message.</summary>
    public OutcomeRedirect? Redirect { get; init; }

    /// <summary>At most one <c>use</c> rule.</summary>
    public IReadOnlyList<FormRule> Rules { get; init => field = DefinitionList.Of(value); } = DefinitionList<FormRule>.Empty;
}

/// <summary>Where an outcome sends the visitor: a content page or a URL.</summary>
public sealed record OutcomeRedirect
{
    /// <summary>The key of a content item.</summary>
    public Guid? Content { get; init; }

    /// <summary>A site-relative or absolute <c>https</c> URL; merge tags may appear only after the host.</summary>
    public string? Url { get; init; }

    /// <summary>How long a message shows before the redirect, in seconds.</summary>
    public int DelaySeconds { get; init; }
}
