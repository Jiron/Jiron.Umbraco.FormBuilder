using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>
/// Jiron Form Builder's host configuration: the <c>JironFormBuilder</c> section of appsettings (section 4.8, ADR-0008,
/// ADR-0042). It is for developers and for locks, and it wins over the backoffice. It is read once and checked when
/// the site starts: a value out of range or a key Jiron Form Builder does not know stops the site with a message that
/// names the key, and a change takes effect when the site restarts.
/// </summary>
public sealed class FormBuilderOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "JironFormBuilder";

    /// <summary>Where uploaded files are kept.</summary>
    public FormBuilderFilesOptions Files { get; } = new();

    /// <summary>Hard limits that editors cannot exceed.</summary>
    public FormBuilderLimitsOptions Limits { get; } = new();

    /// <summary>Settings of field types and action types that the designer cannot change.</summary>
    public FormBuilderDesignerOptions Designer { get; } = new();

    /// <summary>Who holds which permission: the global grants, set from the host (ADR-0048).</summary>
    public FormBuilderAccessOptions Access { get; } = new();

    /// <summary>
    /// Backoffice settings sections that are read-only: <c>general</c>, <c>email</c>, <c>protection</c>, <c>files</c>,
    /// <c>privacy</c>, <c>access</c> (every grant and restriction) or <c>integrations</c>, in any case.
    /// </summary>
    public IList<string> LockedSections { get; } = [];
}

/// <summary>Hard limits that editors cannot exceed (section 4.8).</summary>
public sealed class FormBuilderLimitsOptions
{
    /// <summary>
    /// How large a form may be. Each limit is at least 1 and at most ten times its default. A lowered limit refuses to
    /// save a form above it, and forms stored above it keep working.
    /// </summary>
    public DefinitionLimits Definitions { get; } = new();
}

/// <summary>Locks on the settings that editors give fields and actions in the designer (section 4.8).</summary>
public sealed class FormBuilderDesignerOptions
{
    /// <summary>
    /// Settings fixed for every form, for example the sender of every email action. Each names a kind, a type and a
    /// setting once.
    /// </summary>
    public IList<DesignerSettingLock> SettingLocks { get; } = [];
}

/// <summary>
/// A setting of a field type or an action type that is fixed in every form, for example
/// <c>{ "kind": "action", "type": "email", "setting": "from", "value": "noreply@example.com", "hidden": true }</c>. The
/// designer shows it read-only, or not at all when hidden, a save with another value is refused, and the site uses the
/// locked value whatever a form says.
/// </summary>
public sealed class DesignerSettingLock
{
    /// <summary>The <see cref="Kind"/> of a lock on a field type's setting.</summary>
    public const string FieldKind = "field";

    /// <summary>The <see cref="Kind"/> of a lock on an action type's setting.</summary>
    public const string ActionKind = "action";

    /// <summary>Whether the lock is on a field type (<c>field</c>) or an action type (<c>action</c>), in any case.</summary>
    [AllowedValues(FieldKind, ActionKind)]
    public string? Kind { get; set; }

    /// <summary>The type's alias, for example <c>email</c>: a letter, then letters, digits, '.', '-' and '_', at most 64 characters.</summary>
    public string? Type { get; set; }

    /// <summary>The setting's name in the type's settings, for example <c>from</c>.</summary>
    public string? Setting { get; set; }

    /// <summary>The value every form uses, as text, at most 50,000 characters. Without one, the setting keeps its default.</summary>
    public string? Value { get; set; }

    /// <summary>Hides the setting in the designer instead of showing it read-only.</summary>
    public bool Hidden { get; set; }
}

