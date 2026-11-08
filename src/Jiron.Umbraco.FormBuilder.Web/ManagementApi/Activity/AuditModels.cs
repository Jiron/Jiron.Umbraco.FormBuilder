using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Activity;

/// <summary>A page of the audit log, with the names of the items it names (ADR-0219).</summary>
internal sealed record AuditListResponseModel
{
    /// <summary>The page's actions, the newest first.</summary>
    public required IReadOnlyList<AuditEntryResponseModel> Items { get; init; }

    /// <summary>How many actions match.</summary>
    public required long Total { get; init; }

    /// <summary>
    /// The names of the items the page's actions are on or name in their details, by key: forms, folders, the library's
    /// items, user groups, sites, entries by their number, and fields by their label. An item that is gone has none.
    /// </summary>
    public required IReadOnlyDictionary<Guid, AuditNameResponseModel> Names { get; init; }
}

/// <summary>One recorded action: who did what to which item, and when; never content (ADR-0041).</summary>
internal sealed record AuditEntryResponseModel
{
    /// <summary>The entry's id.</summary>
    public required long Id { get; init; }

    /// <summary>When it happened.</summary>
    public required DateTimeOffset OccurredUtc { get; init; }

    /// <summary>Who did it; null for Jiron Form Builder itself.</summary>
    public Guid? UserKey { get; init; }

    /// <summary>The user's name; null for Jiron Form Builder itself, or a user who is gone.</summary>
    public string? UserName { get; init; }

    /// <summary>What happened: <c>entity.verb</c>, such as <c>form.published</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The kind of item it happened to.</summary>
    public required string EntityType { get; init; }

    /// <summary>The item's key, if it has one.</summary>
    public Guid? EntityKey { get; init; }

    /// <summary>The details: keys, counts, flags and identifiers, as a JSON object.</summary>
    public required JsonElement Details { get; init; }
}

/// <summary>An item's name as the audit log shows it.</summary>
internal sealed record AuditNameResponseModel
{
    /// <summary>
    /// <c>form</c>, <c>folder</c>, <c>optionList</c>, <c>theme</c>, <c>emailLayout</c>, <c>template</c>, <c>userGroup</c>,
    /// <c>site</c>, <c>entry</c> or <c>field</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>Its name; for an entry its form's name, for a field its label.</summary>
    public required string Name { get; init; }

    /// <summary>An entry's number.</summary>
    public long? Number { get; init; }
}
