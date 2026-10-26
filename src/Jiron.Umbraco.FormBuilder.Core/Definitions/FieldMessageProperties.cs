using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>One property of <see cref="FieldMessages"/>: its name, the error kind it is the message of, and its reader.</summary>
/// <param name="Name">Its JSON name.</param>
/// <param name="Kind">The error kind it is the message of.</param>
/// <param name="Read">Reads it from a field's messages.</param>
internal sealed record FieldMessageProperty(string Name, FieldMessageKinds Kind, Func<FieldMessages, LocalizedText?> Read);

/// <summary>
/// The properties of <see cref="FieldMessages"/>, listed once for the save checks and the structural checks; a test
/// holds the list to the record and to <see cref="FieldMessageKinds"/>.
/// </summary>
internal static class FieldMessageProperties
{
    /// <summary>Every property, in the record's order.</summary>
    public static readonly ImmutableArray<FieldMessageProperty> All =
    [
        new("required", FieldMessageKinds.Required, messages => messages.Required),
        new("invalid", FieldMessageKinds.Invalid, messages => messages.Invalid),
        new("minLength", FieldMessageKinds.MinLength, messages => messages.MinLength),
        new("maxLength", FieldMessageKinds.MaxLength, messages => messages.MaxLength),
        new("minWords", FieldMessageKinds.MinWords, messages => messages.MinWords),
        new("maxWords", FieldMessageKinds.MaxWords, messages => messages.MaxWords),
        new("min", FieldMessageKinds.Min, messages => messages.Min),
        new("max", FieldMessageKinds.Max, messages => messages.Max),
        new("step", FieldMessageKinds.Step, messages => messages.Step),
        new("dateWindow", FieldMessageKinds.DateWindow, messages => messages.DateWindow),
        new("selections", FieldMessageKinds.Selections, messages => messages.Selections),
        new("fileType", FieldMessageKinds.FileType, messages => messages.FileType),
        new("fileSize", FieldMessageKinds.FileSize, messages => messages.FileSize),
        new("fileCount", FieldMessageKinds.FileCount, messages => messages.FileCount),
        new("full", FieldMessageKinds.Full, messages => messages.Full),
        new("confirmation", FieldMessageKinds.Confirmation, messages => messages.Confirmation),
    ];
}
