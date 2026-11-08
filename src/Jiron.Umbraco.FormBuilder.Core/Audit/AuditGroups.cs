namespace Jiron.Umbraco.FormBuilder.Core.Audit;

/// <summary>What the audit log's "What" filter offers (ADR-0219).</summary>
internal enum AuditGroup
{
    /// <summary>Forms and folders.</summary>
    Forms,

    /// <summary>Entries, their files, and deliveries.</summary>
    Entries,

    /// <summary>The library: option lists, themes, email layouts and templates.</summary>
    Library,

    /// <summary>Settings, access, and the log itself.</summary>
    Settings,
}

/// <summary>
/// The kinds of item each group of the audit log holds: an action's first word (<c>form</c> of <c>form.published</c>). A unit
/// test fails for an action the product records whose kind no group holds, so every action can be filtered.
/// </summary>
internal static class AuditGroups
{
    /// <summary>The audit action of an export of the log; the details name its filters and the rows written.</summary>
    public const string ExportedAction = "audit.exported";

    /// <summary>The audit entity type of the log itself; the item is null.</summary>
    public const string EntityType = "audit";

    /// <summary>The kinds of item a group holds.</summary>
    public static IReadOnlyList<string> KindsOf(AuditGroup group) => group switch
    {
        AuditGroup.Forms => ["form", "folder"],
        AuditGroup.Entries => ["entry", "entries", "entryFile", "email"],
        AuditGroup.Library => ["optionList", "theme", "emailLayout", "template"],
        AuditGroup.Settings => ["settings", "access", EntityType],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "No such group."),
    };

    /// <summary>The group an action belongs to, by its first word; null for one no group holds.</summary>
    public static AuditGroup? GroupOf(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var kind = action.Split('.')[0];
        foreach (var group in Enum.GetValues<AuditGroup>())
        {
            if (KindsOf(group).Contains(kind, StringComparer.Ordinal))
            {
                return group;
            }
        }

        return null;
    }
}
