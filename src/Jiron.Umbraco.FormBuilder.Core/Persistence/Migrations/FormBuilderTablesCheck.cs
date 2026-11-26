using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Whether the package's tables are there, for the handlers of Umbraco's own notifications that write to them (27.1):
/// while the plan has not run its first step, a change of a document, a type, a member or a user group has nothing of
/// Jiron Form Builder's to change, so those handlers do nothing, and the log hears of it once, as one line without a
/// stack trace, where it used to get a warning for each change. Nothing is lost: the plan's step 4 asks for the rebuild
/// of the "used on" index, which reads every document, and no entry, grant or setting can exist before the tables do.
/// </summary>
internal sealed partial class FormBuilderTablesCheck(IFormBuilderMigrationState migrationState, ILogger<FormBuilderTablesCheck> logger)
{
    private int _told;

    /// <summary>True when the package's tables are there; otherwise false, said to the log once in the process's life.</summary>
    /// <param name="cancellationToken">Cancels the read of the plan's state.</param>
    public async ValueTask<bool> AreThereAsync(CancellationToken cancellationToken = default)
    {
        if (await migrationState.HasTablesAsync(cancellationToken))
        {
            return true;
        }

        if (Interlocked.Exchange(ref _told, 1) == 0)
        {
            LogNotThere(logger);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder's migrations have not run yet, so its tables are not there; until they are, it leaves Umbraco's changes to content, types, members and user groups alone, and rebuilds what content uses once they have run")]
    private static partial void LogNotThere(ILogger logger);
}
