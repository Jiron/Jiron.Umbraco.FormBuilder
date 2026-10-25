using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// Says so at start-up when a load-balanced site keeps uploads in the default folder (ADR-0041, amended in 4.8c): the
/// folder lies on each server's own disk, so a file uploaded on one server is missing on the others. A site counts as
/// load-balanced when this server is a subscriber, or when more than one server is registered as active; a single
/// server, also one that Umbraco's election makes the scheduling publisher, never hears of it.
/// </summary>
internal sealed partial class FileStoreLoadBalancingCheck(
    IOptions<FormBuilderOptions> options,
    IFormBuilderFileStore store,
    IServerRoleAccessor roles,
    IServerRegistrationService registrations,
    IRuntimeState runtime,
    ILogger<FileStoreLoadBalancingCheck> logger)
    : INotificationHandler<UmbracoApplicationStartedNotification>
{
    /// <inheritdoc />
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        if (runtime.Level != RuntimeLevel.Run || store is not PhysicalFileStore || !IsDefault(options.Value.Files.Root))
        {
            return;
        }

        var role = roles.CurrentServerRole;
        if (role != ServerRole.Single && (role == ServerRole.Subscriber || ActiveServers() > 1))
        {
            LogLocalRoot(logger, FormBuilderFilesOptions.DefaultRoot, FormBuilderFilesOptions.RootKey);
        }
    }

    /// <summary>Whether <paramref name="root"/> names the default folder, as written in any letter case and with or without a closing slash.</summary>
    internal static bool IsDefault(string? root) =>
        root is not null && string.Equals(root.Trim().TrimEnd('/', (char)0x5C), FormBuilderFilesOptions.DefaultRoot, StringComparison.OrdinalIgnoreCase);

    // The servers registered as active; none when the registrations cannot be read, so the check never stops a start.
    private int ActiveServers()
    {
        try
        {
            return registrations.GetActiveServers(refresh: true)?.Count() ?? 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogServersUnreadable(logger, exception.GetType().Name);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder keeps uploaded files in its default folder {Root}, on each server's own disk, and this site runs on more than one server: a file uploaded on one server is missing on the others. Set {Setting} to a folder every server shares, or register a file store that keeps the files elsewhere")]
    private static partial void LogLocalRoot(ILogger logger, string root, string setting);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not read Umbraco's server registrations ({Failure}), so it cannot tell whether the default folder for uploads is shared")]
    private static partial void LogServersUnreadable(ILogger logger, string failure);
}
