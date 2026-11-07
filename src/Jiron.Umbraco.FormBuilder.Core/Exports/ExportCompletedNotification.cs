using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Exports;

/// <summary>
/// Published once an export of a form's entries has finished (section 4.9, ADR-0210), streamed or in the background, after
/// its file is written. It names no entry and no answer.
/// </summary>
/// <param name="formKey">The form.</param>
/// <param name="format">The format's alias, such as <c>csv</c>.</param>
/// <param name="entries">How many entries it holds.</param>
/// <param name="userKey">The user who exported.</param>
public sealed class ExportCompletedNotification(Guid formKey, string format, int entries, Guid userKey) : INotification
{
    /// <summary>The form.</summary>
    public Guid FormKey { get; } = formKey;

    /// <summary>The format's alias, such as <c>csv</c>.</summary>
    public string Format { get; } = format;

    /// <summary>How many entries it holds.</summary>
    public int Entries { get; } = entries;

    /// <summary>The user who exported.</summary>
    public Guid UserKey { get; } = userKey;
}
