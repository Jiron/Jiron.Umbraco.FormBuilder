using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>What an attempt runs for (ADR-0158): the action as the site runs it, and the entry.</summary>
public sealed record ActionRunContext
{
    /// <summary>The action, with the host's designer locks applied.</summary>
    public required FormAction Action { get; init; }

    /// <summary>What started it.</summary>
    public required ActionTrigger Trigger { get; init; }

    /// <summary>
    /// The entry to render with: the version it was submitted with, its culture, its page, its stored answers without the
    /// fields the rules hid, and its number. A field kept only until the actions have run is still there.
    /// </summary>
    public required MergeTagScope Scope { get; init; }

    /// <summary>
    /// The number of this attempt, from 1. A retry from the deliveries log starts over at 1, and the run's
    /// <see cref="DeliveryKey"/> stays the same.
    /// </summary>
    public required int Attempt { get; init; }

    /// <summary>
    /// A key that stays the same over the attempts of a run, which a receiver can use to notice a repeat: an attempt that
    /// was cut off may have arrived.
    /// </summary>
    public required string DeliveryKey { get; init; }

    /// <summary>The run, for what it records under it, such as mail in the deliveries log.</summary>
    public long? RunId { get; init; }

    /// <summary>The stored entry, for what it records under it; null when nothing is stored, as in a test run.</summary>
    public long? EntryId { get; init; }
}

/// <summary>The user who asks for a test run.</summary>
/// <param name="Key">The user's key.</param>
/// <param name="Email">The user's address; null when the user has none.</param>
/// <param name="Name">The user's name.</param>
public sealed record ActionTestUser(Guid Key, string? Email, string Name);

/// <summary>What a test run runs for (ADR-0158): the action of a form's draft and a sample entry.</summary>
public sealed record ActionTestContext
{
    /// <summary>The action, with the host's designer locks applied.</summary>
    public required FormAction Action { get; init; }

    /// <summary>A sample entry of the draft, numbered 0, whose answers are made up from the fields' labels; nothing stores it.</summary>
    public required MergeTagScope Scope { get; init; }

    /// <summary>The user who asks.</summary>
    public required ActionTestUser User { get; init; }
}

/// <summary>
/// What an action type's own check of an action's settings reports to (<see cref="IFormBuilderActionType.CheckSettings"/>).
/// Publishing runs it once the settings keep the type's schema and the rules of its attributes; each problem names the
/// value it concerns, which the designer marks.
/// </summary>
public sealed class ActionSettingsCheck
{
    private readonly string _path;
    private readonly DefinitionProblems _problems;

    internal ActionSettingsCheck(FormAction action, FormDefinition definition, string path, DefinitionProblems problems)
    {
        Action = action;
        Definition = definition;
        _path = path;
        _problems = problems;
    }

    /// <summary>The action whose settings are checked.</summary>
    public FormAction Action { get; }

    /// <summary>The form it belongs to, for a setting that names a field.</summary>
    public FormDefinition Definition { get; }

    /// <summary>Refuses a value of the action's settings.</summary>
    /// <param name="setting">
    /// Where the value is in the settings: a setting's name, then <c>.name</c> inside an object of settings and
    /// <c>[index]</c> in a list, such as <c>to</c>, <c>mapping.field</c> or <c>headers[2]</c>.
    /// </param>
    /// <param name="message">What is wrong, as a sentence the editor reads.</param>
    /// <exception cref="ArgumentException">The place is not written that way, or the message is blank.</exception>
    public void Invalid(string setting, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!FieldSettingsCheck.IsSettingPath(setting))
        {
            throw new ArgumentException($"'{setting}' is no place in the settings: a setting's name, then .name or [index].", nameof(setting));
        }

        _problems.Add(DefinitionErrors.SettingInvalid, $"{_path}.{setting}", message);
    }
}
