namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Gives a value of an enum that a setting holds the label the designer shows for it (ADR-0057), as
/// <see cref="FormBuilderSettingAttribute"/> gives a setting its label. Without it, the label is the value's name as C#
/// writes it, such as <c>Vertical</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class FormBuilderChoiceAttribute : Attribute
{
    /// <summary>A choice with its label.</summary>
    /// <param name="label">The label: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</param>
    public FormBuilderChoiceAttribute(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
    }

    /// <summary>The label: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public string Label { get; }
}
