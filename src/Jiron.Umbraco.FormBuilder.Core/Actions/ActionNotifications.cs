using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// Published before an attempt of an action run (section 4.9). A handler that throws is logged and changes nothing about
/// the attempt, and the context holds what the entry holds, so a handler keeps its values out of its logs (D13).
/// </summary>
/// <param name="context">The entry and the action.</param>
/// <param name="type">The action type's alias.</param>
public sealed class ActionRunningNotification(ActionRunContext context, string type) : INotification
{
    /// <summary>The entry and the action.</summary>
    public ActionRunContext Context { get; } = context;

    /// <summary>The action type's alias.</summary>
    public string Type { get; } = type;
}

/// <summary>Published after an attempt that delivered its action (section 4.9).</summary>
/// <param name="context">The entry and the action.</param>
/// <param name="type">The action type's alias.</param>
public sealed class ActionRunCompletedNotification(ActionRunContext context, string type) : INotification
{
    /// <summary>The entry and the action.</summary>
    public ActionRunContext Context { get; } = context;

    /// <summary>The action type's alias.</summary>
    public string Type { get; } = type;
}

/// <summary>Published after an attempt that did not deliver its action, or may not have (section 4.9).</summary>
/// <param name="context">The entry and the action.</param>
/// <param name="type">The action type's alias.</param>
/// <param name="willRetry">Whether another attempt follows after the back-off.</param>
/// <param name="code">Why, as a code; null when the result had none.</param>
public sealed class ActionRunFailedNotification(ActionRunContext context, string type, bool willRetry, string? code) : INotification
{
    /// <summary>The entry and the action.</summary>
    public ActionRunContext Context { get; } = context;

    /// <summary>The action type's alias.</summary>
    public string Type { get; } = type;

    /// <summary>Whether another attempt follows after the back-off; false for a failure for good and an uncertain run.</summary>
    public bool WillRetry { get; } = willRetry;

    /// <summary>Why, as a code; null when the result had none.</summary>
    public string? Code { get; } = code;
}
