namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// How large a definition may be (section 4.3.1). The host can lower or raise them (<c>JironFormBuilder:Limits:Definitions</c>,
/// bound in 4.4); the text limits of ADR-0002 are fixed.
/// </summary>
public sealed record DefinitionLimits
{
    /// <summary>The largest document, in UTF-8 bytes: 2 MB.</summary>
    public int MaxBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>Pages per form.</summary>
    public int MaxPages { get; init; } = 30;

    /// <summary>Groups per form, on all pages.</summary>
    public int MaxGroups { get; init; } = 100;

    /// <summary>Fields per form, counting the fields of repeatable groups and the rows of grids, which store like fields.</summary>
    public int MaxFields { get; init; } = 300;

    /// <summary>A choice field's own options.</summary>
    public int MaxOptionsPerField { get; init; } = 1000;

    /// <summary>Actions per form.</summary>
    public int MaxActions { get; init; } = 50;

    /// <summary>Outcomes per form.</summary>
    public int MaxOutcomes { get; init; } = 50;

    /// <summary>Rules per form: those of pages, groups, fields, actions and outcomes, and the form-level validation rules.</summary>
    public int MaxRules { get; init; } = 200;
}
