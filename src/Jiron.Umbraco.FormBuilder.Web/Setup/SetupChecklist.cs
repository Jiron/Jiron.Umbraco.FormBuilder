using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.Setup;

/// <summary>The five steps of the guided first run (section 4.13), in their order, as the API names them.</summary>
internal enum ChecklistStep
{
    /// <summary>The site can send mail.</summary>
    Email,

    /// <summary>The site has a form.</summary>
    Form,

    /// <summary>A form has received an entry.</summary>
    TestEntry,

    /// <summary>Editors can put forms on pages.</summary>
    FormBlock,

    /// <summary>A team other than the administrators sees entries.</summary>
    Access,
}

/// <summary>One step of the checklist as a user sees it.</summary>
/// <param name="Step">The step.</param>
/// <param name="Done">Whether the site has done it, whoever did.</param>
/// <param name="CanDo">Whether this user may do it; otherwise the Overview says who can.</param>
internal sealed record ChecklistItem(ChecklistStep Step, bool Done, bool CanDo);

/// <summary>The checklist as a user sees it.</summary>
/// <param name="Items">The steps, in their order.</param>
/// <param name="Complete">Whether all were done once; the checklist then stays away, whatever happens to a form later.</param>
/// <param name="PreviewForm">The newest form this user may preview, which the third step opens; null for none.</param>
internal sealed record Checklist(IReadOnlyList<ChecklistItem> Items, bool Complete, Guid? PreviewForm);

/// <summary>
/// What knows when each step of the setup checklist is done (section 4.13, ADR-0165), so that each turns green by itself. Each
/// looks at the site, not at the user: a step that a colleague did is done.
/// </summary>
/// <param name="health">The mail check, which the first step shares with the health check.</param>
/// <param name="forms">The forms.</param>
/// <param name="references">The "used on" index.</param>
/// <param name="contentTypes">Umbraco's content types.</param>
/// <param name="access">The access evaluation.</param>
/// <param name="groups">Umbraco's user groups, whose sections say whether a grant can be used.</param>
internal sealed class SetupDetectors(EmailHealth health, IFormRepository forms, IReferenceRepository references, IContentTypeService contentTypes, IFormBuilderAccessService access, IUserGroupService groups)
{
    /// <summary>Whether the site can send mail: Umbraco's gate passes and a From address is set (ADR-0007).</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> EmailAsync(CancellationToken cancellationToken = default) => health.CanSend && await health.HasFromAsync(cancellationToken);

    /// <summary>Whether a form exists outside the trash.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> FormAsync(CancellationToken cancellationToken = default) => (await forms.GetSetupFactsAsync(cancellationToken)).AnyForm;

    /// <summary>Whether a form ever received an entry, a test entry or a real one; deleting it later leaves the step done.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> TestEntryAsync(CancellationToken cancellationToken = default) => (await forms.GetSetupFactsAsync(cancellationToken)).AnyEntry;

    /// <summary>
    /// Whether editors can put a form on a page: the form block's element type exists, or a page already uses a form through a
    /// site's own type, which the "used on" index knows.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> FormBlockAsync(CancellationToken cancellationToken = default) =>
        contentTypes.Get(FormBlockTypes.ElementTypeAlias) is not null || await references.AnyAsync(cancellationToken);

    /// <summary>
    /// Whether a user group other than the administrators may view entries, anywhere, and can reach them: a grant to a group without
    /// the Forms section gives its users nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> AccessAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in await access.ListGroupsHoldingAsync(FormBuilderPermissions.ViewEntries, cancellationToken))
        {
            if ((await groups.GetAsync(key))?.AllowedSections.Contains(FormBuilderConstants.SectionAlias, StringComparer.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The setup checklist (section 4.13, ADR-0165). It remembers once all five steps were done, so that it does not come back when
/// the last form is deleted; the Overview can still show it on request, with the steps as they are now.
/// </summary>
/// <param name="detectors">What knows each step.</param>
/// <param name="access">The access evaluation.</param>
/// <param name="forms">The forms.</param>
/// <param name="values">The key-value store, which holds that setup is complete.</param>
internal sealed class SetupChecklist(SetupDetectors detectors, IFormBuilderAccessService access, IFormRepository forms, IKeyValueRepository values)
{
    /// <summary>The row that says all five steps were done.</summary>
    public const string CompleteKey = "jfb.setup.complete";

    /// <summary>The checklist for a user.</summary>
    /// <param name="user">Who asks.</param>
    /// <param name="hasSettingsSection">Whether the user has Umbraco's Settings section, which creating the form block needs.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<Checklist> GetAsync(AccessUser user, bool hasSettingsSection, CancellationToken cancellationToken = default)
    {
        var global = await access.GetGlobalAsync(user, cancellationToken);
        var onFolders = await access.GetOnFoldersAsync(user, cancellationToken);
        var designable = await access.GetFormVisibilityAsync(user, FormBuilderPermissions.Design, cancellationToken);
        var preview = designable.IsNothing ? null : await forms.GetNewestAsync(designable, cancellationToken);
        ChecklistItem[] items =
        [
            new(ChecklistStep.Email, await detectors.EmailAsync(cancellationToken), global.HasFlag(FormBuilderPermissions.ManageSettings)),
            new(ChecklistStep.Form, await detectors.FormAsync(cancellationToken), onFolders.HasFlag(FormBuilderPermissions.Design)),
            new(ChecklistStep.TestEntry, await detectors.TestEntryAsync(cancellationToken), preview is not null),
            new(ChecklistStep.FormBlock, await detectors.FormBlockAsync(cancellationToken), global.HasFlag(FormBuilderPermissions.ManageSettings) && hasSettingsSection),
            new(ChecklistStep.Access, await detectors.AccessAsync(cancellationToken), global.HasFlag(FormBuilderPermissions.ManagePermissions)),
        ];
        return new Checklist(items, await CompleteAsync(items, cancellationToken), preview?.Key);
    }

    private async Task<bool> CompleteAsync(ChecklistItem[] items, CancellationToken cancellationToken)
    {
        if (await values.GetAsync(CompleteKey, cancellationToken) is not null)
        {
            return true;
        }

        if (!items.All(item => item.Done))
        {
            return false;
        }

        await values.TryAddAsync(CompleteKey, "1", cancellationToken);
        return true;
    }
}
