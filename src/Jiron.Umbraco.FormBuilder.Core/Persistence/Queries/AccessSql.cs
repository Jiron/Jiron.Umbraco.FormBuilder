using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>
/// The "who may see this form" filter in SQL (ADR-0048): one clause on <c>jfbForm</c> that keeps the forms of an
/// <see cref="AccessScope"/>, so every list filters before paging and counting. The form list uses it, and so will the
/// inbox (11.3).
/// </summary>
internal static class AccessSql
{
    /// <summary>
    /// The clause, or null when the scope keeps every form (or there is none). <paramref name="form"/> is the prefix of
    /// the form table's columns, such as <c>f.</c>, or empty; <paramref name="arg"/> adds a parameter and names it.
    /// </summary>
    /// <remarks>
    /// The folder ids go into the text as integer literals: they are numbers the snapshot read from the database, so no
    /// text a user typed reaches the statement, and a large tree cannot reach SQL Server's limit of 2,100 parameters.
    /// The user's groups are parameters; the scope names only those with a grant on a form of their own, a handful.
    /// </remarks>
    public static string? FormClause(AccessScope? scope, SqlNames n, string form, Func<object?, string> arg)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(arg);
        if (scope is null || scope.IsEverything)
        {
            return null;
        }

        if (scope.IsNothing)
        {
            return "1 = 0";
        }

        if (scope.GroupKeys.Count > PermissionRepository.GroupsPerStatement)
        {
            throw new ArgumentException($"A scope names at most {PermissionRepository.GroupsPerStatement} groups with grants on forms.", nameof(scope));
        }

        var places = new List<string>();
        if (scope.FolderIds.Count > 0)
        {
            places.Add($"{form}{n.C("folderId")} IN ({string.Join(", ", scope.FolderIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))})");
        }

        if (scope.Root)
        {
            places.Add($"{form}{n.C("folderId")} IS NULL");
        }

        var parts = new List<string>();
        if (places.Count > 0)
        {
            parts.Add($"({form}{n.C("isRestricted")} = 0 AND ({string.Join(" OR ", places)}))");
        }

        if (scope.GroupKeys.Count > 0)
        {
            var groups = string.Join(", ", scope.GroupKeys.Select(key => arg(key)));
            parts.Add(
                $"{form}{n.C("key")} IN (SELECT p.{n.C("targetKey")} FROM {n.T(FormBuilderTables.Permission)} p WHERE p.{n.C("targetType")} = '{nameof(PermissionTarget.Form)}' AND p.{n.C("userGroupKey")} IN ({groups}) AND (p.{n.C("permissions")} & {arg((int)scope.AnyOf)}) <> 0)");
        }

        return parts.Count == 1 ? parts[0] : $"({string.Join(" OR ", parts)})";
    }
}
