using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The forms a page uses by the "used on" index (ADR-0017), read synchronously for Umbraco's output cache, which asks
/// its tag providers without a task when it stores a page (ADR-0004): one query in a scope that writes nothing. A form
/// that is gone is not listed.
/// </summary>
/// <param name="scopes">Umbraco's scopes, whose database the read uses.</param>
internal sealed class PageForms(IScopeProvider scopes)
{
    /// <summary>The keys of the forms a content item names, each once, in key order.</summary>
    /// <param name="contentKey">The content item's key.</param>
    public IReadOnlyList<Guid> Of(Guid contentKey)
    {
        using var scope = scopes.CreateScope(autoComplete: true);
        var db = scope.Database;
        var n = new SqlNames(db);
        var keys = db.Fetch<Guid>(
            $"SELECT DISTINCT f.{n.C("key")} FROM {n.T(FormBuilderTables.Reference)} r INNER JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = r.{n.C("formId")} WHERE r.{n.C("contentKey")} = @0",
            contentKey);
        return [.. keys.Order()];
    }
}
