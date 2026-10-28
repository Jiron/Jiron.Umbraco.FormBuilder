using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The answers a submission holds against the other entries (ADR-0091), as <see cref="SubmissionReader"/> names them:
/// each shown answer of a field with a <c>uniquePerForm</c> check that passed the field's other checks, with the indexes
/// of its unique checks, its row, and its values as entries are compared by them; none for an unanswered or hidden
/// field, an answer that fails another check, or a type that does not support the check.
/// </summary>
public sealed class SubmissionUniqueTests
{
    private static readonly Guid Email = Key(1);
    private static readonly Guid Amount = Key(2);
    private static readonly Guid Day = Key(3);
    private static readonly Guid Agree = Key(4);
    private static readonly Guid Nickname = Key(5);
    private static readonly Guid Note = Key(6);
    private static readonly Guid Guests = Key(7);
    private static readonly Guid Badge = Key(8);

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    // An email whose unique check comes second, a number, a date, a checkbox whose type has no such check, a nickname
    // hidden for an amount of 5, a note, and a repeatable group whose badge is unique.
    private static FormDefinition Form { get; } = new()
    {
        Key = Key(100),
        Pages =
        [
            new FormPage
            {
                Key = Key(101),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(102),
                        Fields =
                        [
                            Field(Email, "email", "email") with { Validation = [new PatternCheck { Pattern = "[a-z@.]+" }, new UniquePerFormCheck()] },
                            Unique(Field(Amount, "number", "amount")),
                            Unique(Field(Day, "date", "day")),
                            Unique(Field(Agree, "checkbox", "agree")),
                            Unique(Field(Nickname, "text", "nickname")) with
                            {
                                Rules = [new FormRule { Key = Key(103), Effect = RuleEffect.Hide, Conditions = [new RuleCondition { Field = Amount, Operator = "equals", Value = new ConditionOperand.Text("5") }] }],
                            },
                            Unique(Field(Note, "text", "note")),
                            Field(Guests, "repeater", "guests") with { Fields = [Unique(Field(Badge, "text", "badge"))] },
                        ],
                    },
                ],
            },
        ],
    };

    [Test]
    public async Task EachShownAnswerWithAUniqueCheck_IsNamed_WithItsChecks_ItsRow_AndItsComparedValue()
    {
        var read = await ReadAsync(
            new Dictionary<Guid, PostedAnswer> { [Email] = new(["ada@example.com"]), [Amount] = new(["12.0"]), [Day] = new(["2026-10-25"]), [Agree] = new(["true"]), [Nickname] = new([" Nick "]) },
            [new Dictionary<Guid, PostedAnswer> { [Badge] = new(["A1"]) }, new Dictionary<Guid, PostedAnswer>(), new Dictionary<Guid, PostedAnswer> { [Badge] = new(["a1"]) }]);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(Named(read), Is.EqualTo(new[]
            {
                "email: checks 1: text ada@example.com",
                "amount: checks 0: number 12",
                "day: checks 0: date 2026-10-25T00:00:00 Utc",
                "nickname: checks 0: text Nick",
                "badge@0: checks 0: text A1",
                "badge@2: checks 0: text a1",
            }), "the type's stored value: a number and a date by their columns, a text as the type stores it");
        });
    }

    [Test]
    public async Task AnAnswerThatFailsAnotherCheck_OrIsHidden_IsHeldAgainstNothing()
    {
        var read = await ReadAsync(new Dictionary<Guid, PostedAnswer> { [Email] = new(["Ada@example.com"]), [Amount] = new(["5"]), [Nickname] = new(["Nick"]) }, []);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors.Select(error => (error.Field, error.Check)), Is.EqualTo(new[] { (Email, (int?)0) }), "the pattern, which a capital letter fails");
            Assert.That(Named(read), Is.EqualTo(new[] { "amount: checks 0: number 5" }), "the nickname is hidden");
        });
    }

    private static Task<SubmissionAnswers> ReadAsync(IReadOnlyDictionary<Guid, PostedAnswer> fields, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>> rows)
    {
        var options = new OptionListTestSite().Options();
        var site = MergeTagTestSite.Create(options: options);
        var reader = new SubmissionReader(TestFieldTypes.Registry, site.Service, options, NullLogger<SubmissionReader>.Instance);
        return reader.ReadAsync(new PostedSubmission
        {
            Scope = MergeTagTestSite.Scope(form: Form),
            Now = Now,
            TimeZone = "Europe/Zurich",
            Fields = fields,
            Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> { [Guests] = rows },
        });
    }

    // Each answer held as "alias@row: checks 0,2: text ada", in the order the reader names them.
    private static string[] Named(SubmissionAnswers read) =>
        [.. read.Unique.Select(unique => $"{AliasOf(unique.Field)}{(unique.Row is { } row ? $"@{row}" : "")}: checks {string.Join(",", unique.Checks)}: {string.Join("; ", unique.Values.Select(Shown))}")];

    private static string Shown(AnswerValue value) =>
        value.Number is { } number ? "number " + number.ToString(CultureInfo.InvariantCulture)
        : value.Date is { } date ? $"date {date:s} {date.Kind}"
        : "text " + value.Text;

    private static string AliasOf(Guid key) =>
        Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).Single(field => field.Key == key).Alias!;

    private static FormField Unique(FormField field) => field with { Validation = [new UniquePerFormCheck()] };

    private static FormField Field(Guid key, string type, string alias) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
    };

    private static Guid Key(int number) => Guid.Parse($"73000000-0000-4000-8000-{number:D12}");
}
