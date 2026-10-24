using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Testing.People;

/// <summary>
/// Gives a test host its people once Umbraco runs: the user groups with their grants, the restricted folders, the users
/// and members of <see cref="FormBuilderTestingOptions"/>, through <see cref="TestPeople"/> and <see cref="TestAccess"/>. It logs
/// <c>Jiron Form Builder test people ready</c> when done, or <c>Jiron Form Builder test people failed</c> with the
/// reason, so a global set-up can wait for either line. With nothing configured (the demo, every other host) it does
/// nothing.
/// </summary>
internal sealed partial class TestPeopleSeeder(
    IOptions<FormBuilderTestingOptions> options,
    IRuntimeState runtimeState,
    TestPeople people,
    TestAccess access,
    ILogger<TestPeopleSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (runtimeState.Level != RuntimeLevel.Run || (settings.UserGroups.Count == 0 && settings.RestrictedFolders.Count == 0 && settings.Users.Count == 0 && settings.Members.Count == 0))
        {
            return;
        }

        try
        {
            await SeedAsync(settings);
            LogReady(logger, settings.UserGroups.Count, settings.RestrictedFolders.Count, settings.Users.Count, settings.Members.Count);
        }
        catch (InvalidOperationException exception)
        {
            LogFailed(logger, exception.Message);
            throw;
        }
    }

    /// <summary>
    /// Creates the groups with their global grants, then the restricted folders, the users and the members. Throws
    /// <see cref="InvalidOperationException"/> on any failure.
    /// </summary>
    public async Task SeedAsync(FormBuilderTestingOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var group in settings.UserGroups)
        {
            await people.EnsureUserGroupAsync(group);
            await access.GrantGloballyAsync(group.Alias, group.Permissions);
        }

        foreach (var folder in settings.RestrictedFolders)
        {
            await access.EnsureRestrictedFolderAsync(folder);
        }

        foreach (var user in settings.Users)
        {
            await people.EnsureUserAsync(user);
        }

        foreach (var member in settings.Members)
        {
            await people.EnsureMemberAsync(member);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder test people ready ({GroupCount} groups, {FolderCount} restricted folders, {UserCount} users, {MemberCount} members)")]
    private static partial void LogReady(ILogger logger, int groupCount, int folderCount, int userCount, int memberCount);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Jiron Form Builder test people failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);
}
