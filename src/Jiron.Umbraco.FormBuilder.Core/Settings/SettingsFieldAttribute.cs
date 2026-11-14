namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// A field of a settings section as its screen shows it (ADR-0218): the box it sits in, its place there and its editor.
/// Its label and, unless it has none, its help are the backoffice's keys <c>jfbGlobalSettings_&lt;section&gt;_&lt;field&gt;</c> and <c>...Help</c>, its limits
/// the property's <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/> and
/// <see cref="System.ComponentModel.DataAnnotations.MaxLengthAttribute"/>.
/// </summary>
/// <param name="box">The box on the screen, by a name of the section's own: its heading is the key
/// <c>jfbGlobalSettings_&lt;section&gt;Box_&lt;box&gt;</c>.</param>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SettingsFieldAttribute(string box) : Attribute
{
    /// <summary>The box on the screen; ignored for a field of a nested object, which sits in its object's.</summary>
    public string Box { get; } = box;

    /// <summary>Where the field sits in its box: lower first, then in the order the record declares them.</summary>
    public int Order { get; set; }

    /// <summary>
    /// The editor that draws it; by default the one of its kind (<c>boolean</c>, <c>wholeNumber</c>, <c>text</c>,
    /// <c>choice</c>, <c>list</c>, <c>localizedText</c>, <c>nested</c>). A key, or an object nested deeper than one level,
    /// names its own.
    /// </summary>
    public string? Editor { get; set; }

    /// <summary>A choice's values the screen does not offer, such as <c>inherit</c>, which is how a form takes the site's.</summary>
    public string[] Except { get; set; } = [];

    /// <summary>The values a text may take, which make it a choice of them; none for any text.</summary>
    public string[] Choices { get; set; } = [];

    /// <summary>
    /// The word after a number, such as days, as the backoffice's key <c>jfbGlobalSettings_&lt;section&gt;_&lt;field&gt;Unit</c>;
    /// false for none.
    /// </summary>
    public bool Unit { get; set; }

    /// <summary>
    /// Whether a help line sits under the label, as the key <c>jfbGlobalSettings_&lt;section&gt;_&lt;field&gt;Help</c>; false for a
    /// field whose label says it all.
    /// </summary>
    public bool Help { get; set; } = true;
}

/// <summary>
/// A field of a settings section only the host configuration sets (ADR-0218): no screen shows it, and the wiring test
/// does not ask for one.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class HostOnlyAttribute : Attribute;
