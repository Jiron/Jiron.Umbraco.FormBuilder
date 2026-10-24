using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Thrown when a change has values that are not allowed: a definition a save or a publish refuses, or a form's name,
/// alias or comment (ADR-0044). Nothing was written. Each problem has a stable code, a path and a message; the path is a
/// JSON path into the definition, or <c>name</c>, <c>alias</c> or <c>comment</c>.
/// </summary>
internal sealed class FormBuilderValidationException : Exception
{
    /// <summary>Creates the exception for at least one problem.</summary>
    public FormBuilderValidationException(IReadOnlyList<DefinitionProblem> problems)
        : base(Describe(problems))
    {
        Problems = problems;
    }

    /// <summary>Every problem found, at least one.</summary>
    public IReadOnlyList<DefinitionProblem> Problems { get; }

    /// <summary>The exception for one problem.</summary>
    public static FormBuilderValidationException For(string code, string path, string message) => new([new DefinitionProblem(code, path, message)]);

    private static string Describe(IReadOnlyList<DefinitionProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentOutOfRangeException.ThrowIfZero(problems.Count, nameof(problems));
        var first = problems[0];
        var more = problems.Count == 1 ? string.Empty : $" (and {problems.Count - 1} more)";
        return $"{first.Code} at {first.Path}: {first.Message}{more}";
    }
}

/// <summary>
/// Thrown when a handler of a cancelable notification cancelled a change before it was written (ADR-0044). It carries
/// the messages the handlers gave; nothing was written.
/// </summary>
internal sealed class FormBuilderCancelledException : Exception
{
    /// <summary>Creates the exception with the handlers' messages.</summary>
    public FormBuilderCancelledException(EventMessages messages)
        : this(ArgumentOrThrow(messages).GetAll().Select(message => new CancelMessage(message.Category, message.Message)).ToList())
    {
    }

    private FormBuilderCancelledException(IReadOnlyList<CancelMessage> messages)
        : base(messages.Count == 0 ? "A notification handler cancelled the change." : "A notification handler cancelled the change: " + string.Join(" ", messages.Select(message => message.Message)))
    {
        Messages = messages;
    }

    /// <summary>The handlers' messages, each with its category; empty when a handler only set <c>Cancel</c>.</summary>
    public IReadOnlyList<CancelMessage> Messages { get; }

    private static EventMessages ArgumentOrThrow(EventMessages messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages;
    }
}

/// <summary>A message a handler gave when it cancelled a change.</summary>
/// <param name="Category">The handler's category, such as its name.</param>
/// <param name="Message">Why it cancelled the change.</param>
internal sealed record CancelMessage(string Category, string Message);
