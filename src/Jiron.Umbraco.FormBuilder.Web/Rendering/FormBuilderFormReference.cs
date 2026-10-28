namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// A form a page names: the value of the form picker (6.7), which <c>&lt;jfb-render form="@..."&gt;</c> and
/// <c>Component.InvokeAsync("FormBuilderForm", new { form = ... })</c> take as they take the form's key (ADR-0093).
/// </summary>
/// <param name="Key">The form's key.</param>
public sealed record FormBuilderFormReference(Guid Key)
{
    /// <summary>The form's key with hyphens, as <c>&lt;jfb-render form="..."&gt;</c> reads it.</summary>
    public override string ToString() => Key.ToString("D");
}
