using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// A field of a built-in type with a visitor's answers, read and then checked as the pipeline does (ADR-0058,
/// ADR-0062): the tests of the built-in types' checks state each case as one.
/// </summary>
/// <param name="Type">The type's alias.</param>
/// <param name="Answers">What the visitor gave.</param>
public sealed record ValidationCase(string Type, FieldAnswers Answers)
{
    /// <summary>The site's today: a Monday.</summary>
    public static readonly DateOnly Today = new(2026, 6, 15);

    private static readonly Guid List = new("5f1e3d2c-4b6a-4978-8c5d-e3f2a1b0c9d8");

    /// <summary>The field's settings as JSON; null for its type's defaults.</summary>
    public string? Settings { get; init; }

    /// <summary>
    /// The values of the field's own options; null for a type with options to take them from a library list, whose
    /// answers 5.5 checks.
    /// </summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>A grid's rows.</summary>
    public IReadOnlyList<Guid> Rows { get; init; } = [];

    /// <summary>The field's own checks.</summary>
    public IReadOnlyList<FieldCheck> Checks { get; init; } = [];

    /// <summary>For a field that stores files, the file each value stands for.</summary>
    public ImmutableArray<FieldFile> Files { get; init; } = [];

    /// <summary>The form's other fields' values by key.</summary>
    public IReadOnlyDictionary<Guid, ImmutableArray<string>> Others { get; init; } = ImmutableDictionary<Guid, ImmutableArray<string>>.Empty;

    /// <summary>Whether the field is required.</summary>
    public bool Required { get; init; }

    /// <summary>A file field's case: one upload token for each file.</summary>
    public static ValidationCase Uploads(string? settings, params (string Name, long Size)[] files) =>
        new("file", new FieldAnswers(files.Select((_, i) => $"token-{i + 1}")))
        {
            Settings = settings,
            Files = [.. files.Select(file => new FieldFile(file.Name, file.Size))],
        };

    /// <summary>The error kind's name, as messages and the vectors write it.</summary>
    public static string Name(FieldMessageKinds kind) => FieldMessageProperties.All.Single(property => property.Kind == kind).Name;

    /// <summary>The field as the case describes it.</summary>
    public FormField Field(IFormBuilderFieldType type) => new()
    {
        Key = new Guid("7a3c1e5b-9d2f-4b8a-a6c4-2e8f0d1b3a59"),
        Type = Type,
        Alias = Type,
        Required = Required,
        Settings = Settings is null ? null : SettingsObject.Parse(Settings),
        Options = Options is { } own
            ? new FieldOptions { Items = [.. own.Select(value => new FieldOption { Value = value })] }
            : type.Declaration.Features.HasFlag(FieldFeatures.Options) ? new FieldOptions { Source = OptionSource.List, OptionList = List } : null,
        Rows = [.. Rows.Select(row => new GridRow { Key = row })],
        Validation = Checks,
    };

    /// <summary>The answers read, which must be readable, then checked.</summary>
    internal FieldErrors Run()
    {
        var type = TestFieldTypes.Registry.FindType(Type)!;
        var field = Field(type);
        var read = FieldValues.Read(type, field, Answers);
        Assert.That(read.IsUnreadable, Is.False, "the type reads the answers");
        return FieldValues.Check(type, field, read, new FieldCheckInput(Required, Today) { Files = Files, Values = Others });
    }

    /// <summary>The names of the type's errors, in order.</summary>
    public string[] Errors() => [.. Run().Errors.Select(Name)];

    /// <inheritdoc />
    public override string ToString() => $"{Type} {Settings}";
}
