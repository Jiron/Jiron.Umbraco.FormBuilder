using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The checks of the built-in text, number and date types (ADR-0058, ADR-0062), each run through its type's
/// <c>Validate</c> as the pipeline runs it, at its edges: a value on a limit and one just past it. The shared vectors
/// (<c>tests/vectors/validation.json</c>) hold the browser's runtime to the same; these restate the edges in C# and add
/// what only the server checks, an email's domains, and the types that have no checks of their own.
/// </summary>
public sealed class BuiltInValidationTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    // A Monday.
    private static readonly DateOnly Today = new(2026, 6, 15);

    // The types of ROADMAP 5.2b, each with checks of its own settings.
    private static readonly string[] TypesOfTheStep = ["text", "textarea", "email", "url", "number", "slider", "rating", "scale", "date", "time", "datetime"];

    // Each check at its edges: the type, its settings (null for its defaults), the answer, and the errors in order.
    public static IEnumerable<TestCaseData> Edges()
    {
        static TestCaseData Case(string name, string type, string? settings, string value, params string[] errors) =>
            new TestCaseData(type, settings, value, errors).SetName($"Checks({type}: {name})");

        const string Lengths = """{"minLength":2,"maxLength":5}""";
        yield return Case("at its shortest", "text", Lengths, "ab");
        yield return Case("below its shortest", "text", Lengths, "a", "minLength");
        yield return Case("at its longest", "text", Lengths, "abcde");
        yield return Case("past its longest", "text", Lengths, "abcdef", "maxLength");
        yield return Case("at the default longest", "text", null, new string('a', 255));
        yield return Case("past the default longest", "text", null, new string('a', 256), "maxLength");
        yield return Case("an emoji is two code units", "text", """{"maxLength":1}""", char.ConvertFromUtf32(0x1F600), "maxLength");

        const string Words = """{"minWords":2,"maxWords":3}""";
        yield return Case("at its fewest words", "textarea", Words, "one two");
        yield return Case("below its fewest words", "textarea", Words, "one", "minWords");
        yield return Case("at its most words", "textarea", Words, "one two three");
        yield return Case("past its most words", "textarea", Words, "one\ntwo\tthree four", "maxWords");
        yield return Case("a no-break space parts words", "textarea", """{"maxWords":1}""", "one" + (char)0xA0 + "two", "maxWords");
        yield return Case("a zero-width space parts none", "textarea", """{"maxWords":1}""", "one" + (char)0x200B + "two");
        yield return Case("lengths before words", "textarea", """{"minLength":5,"minWords":2}""", "abc", "minLength", "minWords");
        yield return Case("past the default longest", "textarea", null, new string('a', 4001), "maxLength");

        yield return Case("https by default", "url", null, "https://example.com/path");
        yield return Case("a scheme in capitals", "url", null, "HTTPS://example.com");
        yield return Case("http not allowed by default", "url", null, "http://example.com", "invalid");
        yield return Case("no scheme", "url", null, "example.com", "invalid");
        yield return Case("http allowed", "url", """{"schemes":["https","http"]}""", "http://example.com");
        yield return Case("a scheme not allowed", "url", """{"schemes":["https","http"]}""", "ftp://example.com", "invalid");

        const string Limits = """{"min":"1","max":"10","decimals":2}""";
        yield return Case("at its minimum", "number", Limits, "1");
        yield return Case("below its minimum", "number", Limits, "0.99", "min");
        yield return Case("at its maximum", "number", Limits, "10");
        yield return Case("past its maximum", "number", Limits, "10.01", "max");
        yield return Case("on a step from its minimum", "number", """{"min":"1","step":"3"}""", "4");
        yield return Case("off a step from its minimum", "number", """{"min":"1","step":"3"}""", "5", "step");
        yield return Case("whole numbers by default", "number", null, "2.5", "step");
        yield return Case("its places as its step", "number", """{"decimals":1}""", "2.5");
        yield return Case("past its places", "number", """{"decimals":1}""", "2.55", "step");
        yield return Case("below and off the step", "number", """{"min":"10","step":"5"}""", "3", "min", "step");
        // Publishing refuses these (ADR-0056); a draft's preview checks nothing with them and never fails.
        yield return Case("a step of 0 limits nothing", "number", """{"step":"0"}""", "2.5");
        yield return Case("a step that is no number limits nothing", "number", """{"step":"x"}""", "2.5");
        yield return Case("limits that are no numbers limit nothing", "number", """{"min":"x","max":"y"}""", "-5.5");

        yield return Case("at its default maximum", "slider", null, "100");
        yield return Case("past its default maximum", "slider", null, "101", "max");
        yield return Case("at its default minimum", "slider", null, "0");
        yield return Case("below its default minimum", "slider", null, "-1", "min");
        yield return Case("off its default step", "slider", null, "50.5", "step");
        yield return Case("on a step from its minimum", "slider", """{"min":"10","max":"50","step":"5"}""", "15");
        yield return Case("off a step from its minimum", "slider", """{"min":"10","max":"50","step":"5"}""", "17", "step");

        yield return Case("its lowest", "rating", null, "1");
        yield return Case("below its lowest", "rating", null, "0", "invalid");
        yield return Case("its highest by default", "rating", null, "5");
        yield return Case("past its count", "rating", null, "6", "invalid");
        yield return Case("a half", "rating", null, "2.5", "invalid");
        yield return Case("ten symbols", "rating", """{"count":10}""", "10");

        yield return Case("its start", "scale", null, "0");
        yield return Case("before its start", "scale", null, "-1", "invalid");
        yield return Case("its end", "scale", null, "10");
        yield return Case("past its end", "scale", null, "11", "invalid");
        yield return Case("a half", "scale", null, "5.5", "invalid");
        yield return Case("from one", "scale", """{"from":1,"to":5}""", "0", "invalid");

        const string June = """{"min":"2026-06-01","max":"2026-06-30"}""";
        yield return Case("at its earliest", "date", June, "2026-06-01");
        yield return Case("before its earliest", "date", June, "2026-05-31", "dateWindow");
        yield return Case("at its latest", "date", June, "2026-06-30");
        yield return Case("after its latest", "date", June, "2026-07-01", "dateWindow");
        yield return Case("two days ahead", "date", """{"min":{"today":"+2d"}}""", "2026-06-17");
        yield return Case("before two days ahead", "date", """{"min":{"today":"+2d"}}""", "2026-06-16", "dateWindow");
        yield return Case("a week back", "date", """{"max":{"today":"-1w"}}""", "2026-06-08");
        yield return Case("after a week back", "date", """{"max":{"today":"-1w"}}""", "2026-06-09", "dateWindow");
        yield return Case("a Friday", "date", """{"weekdaysOnly":true}""", "2026-06-12");
        yield return Case("a Saturday", "date", """{"weekdaysOnly":true}""", "2026-06-13", "dateWindow");
        yield return Case("a Sunday", "date", """{"weekdaysOnly":true}""", "2026-06-14", "dateWindow");
        yield return Case("an excluded date", "date", """{"excluded":["2026-12-25"]}""", "2026-12-25", "dateWindow");
        yield return Case("the day before an excluded date", "date", """{"excluded":["2026-12-25"]}""", "2026-12-24");
        yield return Case("a limit past the calendar", "date", """{"max":{"today":"+9999y"}}""", "2026-06-15", "dateWindow");
        yield return Case("a limit that is no date", "date", """{"min":"2026-02-30"}""", "2026-06-15", "dateWindow");

        const string Hours = """{"min":"09:00","max":"17:00"}""";
        yield return Case("at its earliest", "time", Hours, "09:00");
        yield return Case("a second before its earliest", "time", Hours, "08:59:59", "min");
        yield return Case("at its latest", "time", Hours, "17:00");
        yield return Case("a second after its latest", "time", Hours, "17:00:01", "max");
        yield return Case("on a step from its earliest", "time", """{"min":"09:15","stepMinutes":30}""", "09:45");
        yield return Case("off a step from its earliest", "time", """{"min":"09:15","stepMinutes":30}""", "10:00", "step");
        yield return Case("on a step from midnight", "time", """{"stepMinutes":15}""", "23:45");
        yield return Case("seconds off a step", "time", """{"stepMinutes":15}""", "10:15:30", "step");

        yield return Case("the last second of its latest day", "datetime", June, "2026-06-30T23:59:59");
        yield return Case("the first minute after its latest day", "datetime", June, "2026-07-01T00:00", "dateWindow");
        yield return Case("a Saturday", "datetime", """{"weekdaysOnly":true}""", "2026-06-13T10:00", "dateWindow");
        yield return Case("on a half hour", "datetime", """{"stepMinutes":30}""", "2026-06-15T10:30");
        yield return Case("off a half hour", "datetime", """{"stepMinutes":30}""", "2026-06-15T10:31", "step");
        yield return Case("outside and off the step", "datetime", """{"max":"2026-06-30","stepMinutes":30}""", "2026-07-01T10:31", "dateWindow", "step");
    }

    [TestCaseSource(nameof(Edges))]
    public void ABuiltInType_ChecksItsSettings_AtTheirEdges(string type, string? settings, string value, string[] errors) =>
        Assert.That(Errors(type, settings, new FieldAnswers([value])), Is.EqualTo(errors));

    [Test]
    public void TheEdges_CoverEveryTypeOfTheStep() =>
        Assert.That(Edges().Select(data => (string)data.Arguments[0]!).Append("email").Distinct(), Is.EquivalentTo(TypesOfTheStep));

    [TestCase("anna@example.com")]
    [TestCase("ANNA@Example.COM")]
    public void AnEmail_TypedAgain_IsTheSameAddress_InAnyLetterCase(string again) =>
        Assert.That(Errors("email", """{"confirm":true}""", Confirmed("anna@example.com", again)), Is.Empty);

    [Test]
    public void AnEmail_TypedAgainDifferently_NotAtAll_OrTwice_FailsItsConfirmation()
    {
        const string Confirm = """{"confirm":true}""";

        Assert.Multiple(() =>
        {
            Assert.That(Errors("email", Confirm, Confirmed("anna@example.com", "ana@example.com")), Is.EqualTo(new[] { "confirmation" }));
            Assert.That(Errors("email", Confirm, new FieldAnswers(["anna@example.com"])), Is.EqualTo(new[] { "confirmation" }));
            Assert.That(Errors("email", Confirm, Confirmed("anna@example.com", "anna@example.com", "anna@example.com")), Is.EqualTo(new[] { "confirmation" }));
            Assert.That(Errors("email", null, Confirmed("anna@example.com", "other@example.com")), Is.Empty, "a field that does not confirm ignores the part");
        });
    }

    // An email field's domains, which stay on the server (ADR-0062): a listed domain and every domain below it.
    [TestCase("allow", "anna@example.com", true)]
    [TestCase("allow", "anna@mail.example.com", true)]
    [TestCase("allow", "anna@EXAMPLE.com", true)]
    [TestCase("allow", "anna@notexample.com", false)]
    [TestCase("allow", "anna@example.com.other.test", false)]
    [TestCase("allow", "anna@example.org", false)]
    [TestCase("allow", "anna", false)]
    [TestCase("allow", "anna@", false)]
    [TestCase("block", "anna@example.com", false)]
    [TestCase("block", "anna@mail.example.com", false)]
    [TestCase("block", "anna@example.org", true)]
    [TestCase("block", "anna", true)]
    [TestCase("any", "anna@example.org", true)]
    public void AnEmail_TakesOnlyTheDomainsItsFieldTakes(string mode, string address, bool taken) =>
        Assert.That(Errors("email", $$$"""{"domains":{"mode":"{{{mode}}}","list":["example.com"]}}""", new FieldAnswers([address])), Is.EqualTo(taken ? Array.Empty<string>() : ["invalid"]));

    [Test]
    public void AnEmail_ComparesInternationalDomains_InTheirAsciiForm()
    {
        var unicode = "b" + (char)0xFC + "cher.ch";
        var allowUnicode = $$$"""{"domains":{"mode":"allow","list":["{{{unicode}}}"]}}""";
        const string AllowAscii = """{"domains":{"mode":"allow","list":["xn--bcher-kva.ch"]}}""";

        Assert.Multiple(() =>
        {
            Assert.That(Errors("email", allowUnicode, new FieldAnswers(["anna@xn--bcher-kva.ch"])), Is.Empty);
            Assert.That(Errors("email", allowUnicode, new FieldAnswers(["anna@" + unicode])), Is.Empty);
            Assert.That(Errors("email", allowUnicode, new FieldAnswers(["anna@shop." + unicode])), Is.Empty);
            Assert.That(Errors("email", AllowAscii, new FieldAnswers(["anna@" + unicode])), Is.Empty);
            Assert.That(Errors("email", allowUnicode, new FieldAnswers(["anna@bucher.ch"])), Is.EqualTo(new[] { "invalid" }));
        });
    }

    [Test]
    public void AnEmail_FailsItsConfirmation_BeforeItsDomain() =>
        Assert.That(
            Errors("email", """{"confirm":true,"domains":{"mode":"block","list":["example.com"]}}""", Confirmed("anna@example.com", "ana@example.com")),
            Is.EqualTo(new[] { "confirmation", "invalid" }));

    // The types that have no checks of their own, each with an answer it reads (ADR-0058).
    [TestCase("phone", "not a phone number at all")]
    [TestCase("checkbox", "true")]
    [TestCase("yesno", "no")]
    [TestCase("signature", "an-upload-token")]
    [TestCase("hidden", "anything")]
    [TestCase("consent", "true", Hash)]
    public void ATypeWithoutChecksOfItsOwn_FindsNothing(string type, string value, string? wording = null)
    {
        var answers = wording is null ? new FieldAnswers([value]) : new FieldAnswers([value], [new(ConsentFieldType.WordingPart, [wording])]);

        Assert.That(Errors(type, null, answers, required: true), Is.Empty);
    }

    private static FieldAnswers Confirmed(string address, params string[] again) =>
        new([address], [new(EmailFieldType.ConfirmPart, again)]);

    // The errors of a field of a built-in type, by name, its answers read and then checked as the pipeline does.
    private static string[] Errors(string type, string? settings, FieldAnswers answers, bool required = false)
    {
        var builtIn = TestFieldTypes.Registry.FindType(type)!;
        var field = new FormField
        {
            Key = Guid.NewGuid(),
            Type = type,
            Alias = type,
            Required = required,
            Settings = settings is null ? null : SettingsObject.Parse(settings),
        };
        var read = FieldValues.Read(builtIn, field, answers);
        Assert.That(read.IsUnreadable, Is.False, "the type reads the answer");
        // A signature's upload token stands for its PNG, which the pipeline hands over with it.
        var files = builtIn.Declaration.Storage == FieldStorageKind.File ? read.Values.Select(_ => new FieldFile("signature.png", 4096)).ToImmutableArray() : [];
        return [.. FieldValues.Check(builtIn, field, read, new FieldCheckInput(required, Today) { Files = files }).Errors.Select(kind => FieldMessageProperties.All.Single(message => message.Kind == kind).Name)];
    }
}
