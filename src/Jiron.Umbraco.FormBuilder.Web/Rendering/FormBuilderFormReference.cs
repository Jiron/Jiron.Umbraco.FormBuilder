namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// A form a page names: the value of the form picker, which <c>&lt;jfb-render picked="..."&gt;</c> and
/// <c>Component.InvokeAsync("FormBuilderForm", new { form = ... })</c> take with what the picker's settings give every
/// form it places (ADR-0129); <c>&lt;jfb-render form="@..."&gt;</c> reads it as the form's key alone (ADR-0093).
/// </summary>
/// <param name="Key">The form's key.</param>
public sealed record FormBuilderFormReference(Guid Key)
{
    /// <summary>The theme the placement names, which the form renders in unless the page names one itself; null for the form's own.</summary>
    public string? Theme { get; init; }

    /// <summary>The page a visitor goes to after a send, in place of the form's outcome; null for the outcome.</summary>
    public Guid? Redirect { get; init; }

    /// <summary>The form's key with hyphens, as <c>&lt;jfb-render form="..."&gt;</c> reads it.</summary>
    public override string ToString() => Key.ToString("D");
}
