using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>The pages that use a form, as a user may see them (ADR-0017).</summary>
/// <param name="Shown">The pages inside the user's content start nodes, by name.</param>
/// <param name="NotShown">How many more pages use the form, outside the user's content start nodes.</param>
internal sealed record FormUses(IReadOnlyList<IDocumentEntitySlim> Shown, int NotShown);

/// <summary>
/// Reads the "used on" index for a user (ADR-0017): the pages come from Umbraco when asked, so a renamed or republished
/// page needs no new rows, and a page outside the user's content start nodes is counted but not named. A row whose page
/// has gone meanwhile is left out.
/// </summary>
/// <param name="references">The index's rows.</param>
/// <param name="entities">Umbraco's entities, which tell a page's name, type, state and path.</param>
/// <param name="appCaches">Umbraco's caches, which hold the users' start nodes.</param>
internal sealed class FormUsage(IReferenceRepository references, IEntityService entities, AppCaches appCaches)
{
    /// <summary>The pages that use a form, for a user.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="user">The user who asks.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<FormUses> OfAsync(Guid formKey, IUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var keys = await references.ContentOfAsync(formKey, cancellationToken);
        var shown = new List<IDocumentEntitySlim>();
        var notShown = 0;
        foreach (var batch in keys.Chunk(PageRequest.MaxTake))
        {
            foreach (var page in entities.GetAll(UmbracoObjectTypes.Document, batch).OfType<IDocumentEntitySlim>())
            {
                if (user.HasContentPathAccess(page, entities, appCaches))
                {
                    shown.Add(page);
                }
                else
                {
                    notShown++;
                }
            }
        }

        return new FormUses([.. shown.OrderBy(page => page.Name, StringComparer.OrdinalIgnoreCase).ThenBy(page => page.Key)], notShown);
    }
}
