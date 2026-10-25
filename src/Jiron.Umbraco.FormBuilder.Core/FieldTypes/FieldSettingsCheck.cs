using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type's own check of a field's settings reports to (ADR-0056). Publishing runs it once the settings keep
/// the type's schema and the rules of its attributes, so they read as the type's settings class; each problem names the
/// value it concerns, which the designer marks.
/// </summary>
public sealed partial class FieldSettingsCheck
{
    private readonly string _path;
    private readonly DefinitionProblems _problems;

    internal FieldSettingsCheck(FormField field, string path, DefinitionProblems problems)
    {
        Field = field;
        _path = path;
        _problems = problems;
    }

    /// <summary>The field whose settings are checked, with its options, rows and child fields.</summary>
    public FormField Field { get; }

    /// <summary>Refuses a value of the field's settings.</summary>
    /// <param name="setting">
    /// Where the value is in the settings: a setting's name, then <c>.name</c> inside an object of settings and
    /// <c>[index]</c> in a list, such as <c>max</c>, <c>domains.list</c> or <c>allowed[2]</c>.
    /// </param>
    /// <param name="message">What is wrong, as a sentence the editor reads.</param>
    /// <exception cref="ArgumentException">The place is not written that way, or the message is blank.</exception>
    public void Invalid(string setting, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (setting is null || !SettingPath().IsMatch(setting))
        {
            throw new ArgumentException($"'{setting}' is no place in the settings: a setting's name, then .name or [index].", nameof(setting));
        }

        _problems.Add(DefinitionErrors.SettingInvalid, $"{_path}.{setting}", message);
    }

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*|\[(?:0|[1-9][0-9]{0,8})\])*$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingPath();
}
