using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// Forms and answers for the tests of the <c>field</c> source and <c>entry.answers</c> (ADR-0066): fields of any type
/// with the settings, options, rows and children a test gives, on one page in one group, and their answers.
/// </summary>
internal static class MergeTagAnswerForms
{
    private static int _next;

    /// <summary>A key of its own for each field, option row and form element a test makes.</summary>
    public static Guid NewKey() => new($"a0000000-0000-4000-8000-{Interlocked.Increment(ref _next):D12}");

    /// <summary>A field of a type, with its alias and label, and the settings as JSON when given.</summary>
    public static FormField Field(string type, string alias, string? label = null, string? settings = null) => new()
    {
        Key = NewKey(),
        Type = type,
        Alias = alias,
        Label = label is null ? null : new LocalizedText(label),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    /// <summary>Options with values and labels, each label its default text.</summary>
    public static FieldOptions Options(params (string Value, string Label)[] options) =>
        new() { Items = [.. options.Select(option => new FieldOption { Value = option.Value, Label = new LocalizedText(option.Label) })] };

    /// <summary>A text in cultures, the default first: <c>("", "Yes")</c>, <c>("de", "Ja")</c>.</summary>
    public static LocalizedText Text(params (string Culture, string Text)[] texts) =>
        new(texts.Select(text => new KeyValuePair<string, string>(text.Culture, text.Text)));

    /// <summary>The form of the merge-tag tests with the fields on one page in one group.</summary>
    public static FormDefinition Form(params FormField[] fields) => MergeTagTestSite.Form() with
    {
        Pages = [new FormPage { Key = NewKey(), Groups = [new FormGroup { Key = NewKey(), Fields = [.. fields] }] }],
    };

    /// <summary>A field's answer of texts, in a row of a repeatable group's child when given.</summary>
    public static MergeTagAnswer Answer(FormField field, params string[] values) => new(field.Key, [.. values.Select(value => new FieldValue(value))]);

    /// <summary>A repeatable group's child's answer in a row.</summary>
    public static MergeTagAnswer InRow(FormField child, int row, params string[] values) => new(child.Key, [.. values.Select(value => new FieldValue(value))], row: row);

    /// <summary>The answers, and the fields the rules hid.</summary>
    public static MergeTagAnswers Answers(params MergeTagAnswer[] answers) => new(answers);

    /// <summary>The scope of a form with answers, in a culture, with the sensitive fields an action names to include.</summary>
    public static MergeTagScope Scope(FormDefinition form, MergeTagAnswers answers, string culture = "en-GB", IReadOnlySet<Guid>? included = null) =>
        MergeTagTestSite.Scope(form: form, culture: culture, answers: answers, includedSensitive: included);
}
