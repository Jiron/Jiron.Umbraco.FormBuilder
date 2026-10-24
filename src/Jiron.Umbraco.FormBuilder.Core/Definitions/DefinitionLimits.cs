namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// How large a definition may be (section 4.3.1). The host can lower them, or raise them up to <see cref="Ceiling"/>
/// (<c>JironFormBuilder:Limits:Definitions</c>, ADR-0042); the text limits of ADR-0002 are fixed.
/// </summary>
public sealed record DefinitionLimits
{
    /// <summary>How many times its default a host may raise a limit.</summary>
    public const int CeilingFactor = 10;

    /// <summary>
    /// The highest limits a host may set: ten times each default. What the site stored is read with them, so a lowered
    /// limit never makes a stored definition unreadable (ADR-0042).
    /// </summary>
    public static DefinitionLimits Ceiling { get; } = Times(new DefinitionLimits(), CeilingFactor);

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

    private static DefinitionLimits Times(DefinitionLimits limits, int factor) => new()
    {
        MaxBytes = limits.MaxBytes * factor,
        MaxPages = limits.MaxPages * factor,
        MaxGroups = limits.MaxGroups * factor,
        MaxFields = limits.MaxFields * factor,
        MaxOptionsPerField = limits.MaxOptionsPerField * factor,
        MaxActions = limits.MaxActions * factor,
        MaxOutcomes = limits.MaxOutcomes * factor,
        MaxRules = limits.MaxRules * factor,
    };
}
