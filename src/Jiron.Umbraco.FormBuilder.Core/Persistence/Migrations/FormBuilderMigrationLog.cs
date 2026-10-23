using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>The migration steps' log messages.</summary>
internal static partial class FormBuilderMigrationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: created table {Table}")]
    public static partial void TableCreated(ILogger logger, string table);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: table {Table} exists, skipped")]
    public static partial void TableExists(ILogger logger, string table);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: added foreign key {Key} to {Table}")]
    public static partial void ForeignKeyAdded(ILogger logger, string table, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: created index {Index} on {Table}")]
    public static partial void IndexCreated(ILogger logger, string table, string index);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: index {Index} on {Table} exists, skipped")]
    public static partial void IndexExists(ILogger logger, string table, string index);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: {Added} global settings rows added")]
    public static partial void DefaultSettings(ILogger logger, int added);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: the Forms section was granted before, skipped")]
    public static partial void SectionGrantedBefore(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder migration: Forms granted to user group {Alias}")]
    public static partial void SectionGranted(ILogger logger, string alias);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder migration: user group {Alias} does not exist; nobody was granted the Forms section")]
    public static partial void GroupMissing(ILogger logger, string alias);
}
