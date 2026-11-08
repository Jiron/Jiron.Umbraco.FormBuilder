using Jiron.Umbraco.FormBuilder.Core.Forms;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// What of the library a form's draft and live version use (ADR-0214): written in the transaction of the write that
// changes the version, so the rows never name a version the form no longer has.
internal sealed partial class FormRepository
{
    /// <summary>Replaces what the form's draft or live version uses with what <paramref name="uses"/> names.</summary>
    private static async Task WriteUsesAsync(IUmbracoDatabase db, SqlNames n, int formId, FormVersionKind version, IReadOnlyList<LibraryUse> uses)
    {
        await ForgetUsesAsync(db, n, formId, version);
        foreach (var use in uses)
        {
            await db.ExecAsync(
                $"INSERT INTO {n.T(FormBuilderTables.FormLibraryUse)} ({n.C("formId")}, {n.C("versionKind")}, {n.C("itemKind")}, {n.C("itemKey")}, {n.C("uses")}) VALUES (@0, @1, @2, @3, @4)",
                formId,
                StoredNames.Of(version),
                StoredNames.Of(use.Kind),
                use.Key,
                use.Count);
        }
    }

    /// <summary>Forgets what the form's draft or live version uses, for a version it no longer has.</summary>
    private static Task<int> ForgetUsesAsync(IUmbracoDatabase db, SqlNames n, int formId, FormVersionKind version) =>
        db.ExecAsync(
            $"DELETE FROM {n.T(FormBuilderTables.FormLibraryUse)} WHERE {n.C("formId")} = @0 AND {n.C("versionKind")} = @1",
            formId,
            StoredNames.Of(version));
}
