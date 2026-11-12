using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Actions;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.AuthorizationStatus;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>What a backoffice user lacks in Umbraco for what a content action may do (ADR-0235).</summary>
internal enum ContentRightsGap
{
    /// <summary>Umbraco's Content section.</summary>
    ContentSection,

    /// <summary>Creating where the action puts its items, and updating or publishing them where it does that too.</summary>
    Content,

    /// <summary>Every language of the site, for a document type that varies by culture.</summary>
    Languages,

    /// <summary>Umbraco's Media section, for an action that puts uploaded files in the media library.</summary>
    MediaSection,

    /// <summary>The media folder the action puts uploaded files in.</summary>
    Media,
}

/// <summary>
/// Holds what a content action may do against the backoffice user who sets it up, publishes it or runs it from the preview
/// (ADR-0235). A run saves and publishes content as the site itself, so the person who decides where it goes needs Umbraco's
/// own rights there: the Content section; create on its parent, or on the item its reach starts at and every item below it;
/// update where it updates an item of the same name, and publish where it publishes; every language for a document type that
/// varies by culture; and the Media section with the media folder where it stores uploaded files.
/// </summary>
/// <param name="users">Umbraco's users.</param>
/// <param name="content">Umbraco's content permissions.</param>
/// <param name="media">Umbraco's media permissions.</param>
/// <param name="contentTypes">The document types, to tell whether the action's varies by culture.</param>
/// <param name="languages">The site's languages.</param>
internal sealed class ContentActionRights(
    IUserService users,
    IContentPermissionService content,
    IMediaPermissionService media,
    IContentTypeService contentTypes,
    ILanguageService languages)
{
    /// <summary>The first thing the user lacks for the action, or null when they hold all of it.</summary>
    /// <param name="userKey">The backoffice user.</param>
    /// <param name="settings">The action's settings.</param>
    /// <param name="form">The definition the action belongs to, whose fields tell whether it stores files.</param>
    public async Task<ContentRightsGap?> MissingAsync(Guid userKey, ContentActionSettings settings, FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(form);
        if (await users.GetAsync(userKey) is not { } user || !user.AllowedSections.Contains(Constants.Applications.Content))
        {
            return ContentRightsGap.ContentSection;
        }

        if (!await MayWriteContentAsync(user, settings))
        {
            return ContentRightsGap.Content;
        }

        if (!string.IsNullOrWhiteSpace(settings.DocumentType) && contentTypes.Get(settings.DocumentType) is { } type && type.VariesByCulture()
            && await content.AuthorizeCultureAccessAsync(user, (await languages.GetAllAsync()).Select(language => language.IsoCode).ToHashSet(StringComparer.OrdinalIgnoreCase)) != ContentAuthorizationStatus.Success)
        {
            return ContentRightsGap.Languages;
        }

        if (settings.MediaFolder is null && !ContentActionType.PutsFilesInMedia(settings, form))
        {
            return null;
        }

        if (!user.AllowedSections.Contains(Constants.Applications.Media))
        {
            return ContentRightsGap.MediaSection;
        }

        var folder = settings.MediaFolder is { } key ? await media.AuthorizeAccessAsync(user, key) : await media.AuthorizeRootAccessAsync(user);
        return folder is MediaAuthorizationStatus.Success or MediaAuthorizationStatus.NotFound ? null : ContentRightsGap.Media;
    }

    // Create where the items go, with update and publish where the action does those too. A picked parent, or the content root
    // without one; for the visitor's page or its site, the item the reach starts at and every item below it, as any of them can be
    // the page. An item that is gone answers as allowed: the action fails where it runs, and its form check says so.
    private async Task<bool> MayWriteContentAsync(IUser user, ContentActionSettings settings)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal) { ActionNew.ActionLetter };
        if (settings.UpdateExisting)
        {
            needed.Add(ActionUpdate.ActionLetter);
        }

        if (settings.Publish)
        {
            needed.Add(ActionPublish.ActionLetter);
        }

        if (settings.ParentSource == ContentParentSource.Picked)
        {
            var at = settings.Parent is { } parent ? await content.AuthorizeAccessAsync(user, [parent], needed) : await content.AuthorizeRootAccessAsync(user, needed);
            return Allows(at);
        }

        // Without the item its reach starts at, a run creates nothing (the action fails), so there is nothing to hold.
        if (settings.Parent is not { } top)
        {
            return true;
        }

        return Allows(await content.AuthorizeAccessAsync(user, [top], needed)) && Allows(await content.AuthorizeDescendantsAccessAsync(user, top, needed));
    }

    private static bool Allows(ContentAuthorizationStatus status) => status is ContentAuthorizationStatus.Success or ContentAuthorizationStatus.NotFound;
}
