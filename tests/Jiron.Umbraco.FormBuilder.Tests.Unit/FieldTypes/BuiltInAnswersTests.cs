using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The answers the built-in field types take and how they read them (ADR-0058): the longest answer of every built-in
/// type, restated apart from the code, and the reading of choices, parts, grids, rankings, ticks and consents, each run
/// as the pipeline runs it, so every value read is one its type's storage kind holds.
/// </summary>
public sealed class BuiltInAnswersTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly Guid First = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // The longest text and the most texts of every built-in type as ADR-0058 lists them; null for a type that takes no
    // answer. A grid case names its rows; the settings are those that change the limits.
    public static IEnumerable<TestCaseData> Longest()
    {
        static TestCaseData Case(string type, string? settings, int? longest, int? most, int rows = 0) =>
            new TestCaseData(type, settings, rows, longest, most).SetName($"Longest({type}{(settings is null ? string.Empty : " " + settings)}{(rows == 0 ? string.Empty : $", {rows} rows")})");

        yield return Case("text", null, 4000, 1);
        yield return Case("textarea", null, 100000, 1);
        yield return Case("email", null, 254, 1);
        yield return Case("email", """{"confirm":true}""", 254, 2);
        yield return Case("phone", null, 64, 1);
        yield return Case("url", null, 2048, 1);
        yield return Case("number", null, 64, 1);
        yield return Case("rating", null, 64, 1);
        yield return Case("scale", null, 64, 1);
        yield return Case("slider", null, 64, 1);
        yield return Case("date", null, 10, 1);
        yield return Case("time", null, 8, 1);
        yield return Case("datetime", null, 19, 1);
        yield return Case("dropdown", null, 200, 1);
        yield return Case("dropdown", """{"multiple":true}""", 200, 10000);
        yield return Case("radio", null, 200, 1);
        yield return Case("radio", """{"other":true}""", 2000, 2);
        yield return Case("checkboxes", null, 200, 10000);
        yield return Case("checkboxes", """{"other":true}""", 2000, 10001);
        yield return Case("grid", null, 200, 3, rows: 3);
        yield return Case("grid", """{"multiple":true}""", 200, 30000, rows: 3);
        yield return Case("grid", null, null, null);
        yield return Case("ranking", null, 200, 10000);
        yield return Case("checkbox", null, 5, 1);
        yield return Case("yesno", null, 3, 1);
        yield return Case("consent", null, 64, 2);
        yield return Case("country", null, 2, 1);
        yield return Case("name", null, 200, 4);
        yield return Case("address", null, 200, 6);
        yield return Case("file", null, 1024, 20);
        yield return Case("file", """{"maxFiles":5}""", 1024, 20);
        yield return Case("signature", null, 1024, 1);
        yield return Case("hidden", null, 2000, 1);
        foreach (var none in new[] { "calculation", "repeater", "heading", "paragraph", "divider", "image", "captcha" })
        {
            yield return Case(none, null, null, null);
        }
    }

    [TestCaseSource(nameof(Longest))]
    public void EveryBuiltInType_TakesTheLongestAnswerTheDecisionLists(string type, string? settings, int rows, int? longest, int? most)
    {
        var limits = Type(type).AnswerLimits(Field(type, settings, rows));

        Assert.That(limits, Is.EqualTo(longest is null ? FieldAnswerLimits.None : new FieldAnswerLimits(longest.Value, most!.Value)));
    }

    [Test]
    public void TheLongestAnswers_ListEveryBuiltInType() =>
        Assert.That(Longest().Select(data => (string)data.Arguments[0]!).Distinct(), Is.EquivalentTo(TestFieldTypes.Registry.All.Select(type => type.Alias)));

    [TestCase("checkbox", "true", "true")]
    [TestCase("checkbox", "on", null)]
    [TestCase("date", "2026-10-25", "2026-10-25")]
    [TestCase("time", "08:30", "08:30")]
    [TestCase("time", "08:30:15", "08:30:15")]
    [TestCase("datetime", "2026-10-25T08:30", "2026-10-25T08:30")]
    [TestCase("rating", "4", "4")]
    [TestCase("slider", "2.50", "2.5")]
    [TestCase("yesno", "yes", "yes")]
    [TestCase("yesno", "no", "no")]
    [TestCase("text", " As typed ", " As typed ")]
    [TestCase("country", "CH", "CH")]
    [TestCase("hidden", "campaign-7", "campaign-7")]
    public void ABuiltInType_ReadsItsWireForm(string type, string posted, string? value)
    {
        var read = Read(type, null, new FieldAnswers([posted]));

        Assert.Multiple(() =>
        {
            Assert.That(read.IsUnreadable, Is.False);
            Assert.That(Texts(read), Is.EqualTo(value is null ? Array.Empty<string>() : new[] { value }));
        });
    }

    [TestCase("calculation", "5")]
    [TestCase("repeater", "row")]
    [TestCase("heading", "text")]
    public void ATypeThatTakesNoAnswer_IgnoresWhatIsPosted(string type, string posted)
    {
        var read = Read(type, null, new FieldAnswers([posted]));

        Assert.Multiple(() =>
        {
            Assert.That(read.Values, Is.Empty, "the server recomputes a calculation, and a repeater's fields take its answers");
            Assert.That(read.IsUnreadable, Is.False);
        });
    }

    [TestCase("checkbox", "true", "true")]
    [TestCase("date", "2026-02-30")]
    [TestCase("date", "25.10.2026")]
    [TestCase("time", "8:30")]
    [TestCase("time", "24:00")]
    [TestCase("datetime", "2026-10-25 08:30")]
    [TestCase("number", "1,5")]
    [TestCase("yesno", "maybe")]
    [TestCase("yesno", "yes", "no")]
    [TestCase("text", "a", "b")]
    [TestCase("dropdown", "a", "b")]
    [TestCase("country", "ch")]
    [TestCase("country", "ZZ")]
    [TestCase("country", "EU")]
    public void ABuiltInType_CannotRead(string type, params string[] posted) =>
        Assert.That(Read(type, null, new FieldAnswers(posted)).IsUnreadable, Is.True);

    [Test]
    public void ADropdownThatTakesSeveral_ReadsThemInOrder() =>
        Assert.That(Texts(Read("dropdown", """{"multiple":true}""", new FieldAnswers(["b", "a"]))), Is.EqualTo(new[] { "b", "a" }));

    [Test]
    public void Files_AreTheirUploadTokens() =>
        Assert.That(Texts(Read("file", """{"maxFiles":2}""", new FieldAnswers(["token-1", "token-2"]))), Is.EqualTo(new[] { "token-1", "token-2" }));

    [Test]
    public void AnEmail_IsTheAddress_TheConfirmationLeftToItsCheck() =>
        Assert.That(Texts(Read("email", """{"confirm":true}""", new FieldAnswers(["ada@example.com"], [Part("confirm", "ada@example.org")]))), Is.EqualTo(new[] { "ada@example.com" }));

    [Test]
    public void RadioButtons_TakeTheOtherText_InPlaceOfAnOption()
    {
        const string Other = """{"other":true}""";

        Assert.Multiple(() =>
        {
            Assert.That(Texts(Read("radio", Other, new FieldAnswers([], [Part("other", "My own")]))), Is.EqualTo(new[] { "My own" }));
            Assert.That(Texts(Read("radio", Other, new FieldAnswers(["a"], [Part("other", "My own")]))), Is.EqualTo(new[] { "a" }), "a chosen option wins");
            Assert.That(Texts(Read("radio", null, new FieldAnswers([], [Part("other", "My own")]))), Is.Empty, "a field without Other ignores it");
            Assert.That(Read("radio", Other, new FieldAnswers(["a", "b"])).IsUnreadable, Is.True, "two options");
            Assert.That(Read("radio", Other, new FieldAnswers([], [Part("other", "x", "y")])).IsUnreadable, Is.True, "two Other texts");
        });
    }

    [Test]
    public void Checkboxes_TakeTheOtherText_AfterTheOptions()
    {
        const string Other = """{"other":true}""";

        Assert.Multiple(() =>
        {
            Assert.That(Texts(Read("checkboxes", Other, new FieldAnswers(["b", "a"], [Part("other", "My own")]))), Is.EqualTo(new[] { "b", "a", "My own" }));
            Assert.That(Texts(Read("checkboxes", Other, new FieldAnswers(["a"], [Part("other", "a")]))), Is.EqualTo(new[] { "a" }), "an Other text that repeats an option");
            Assert.That(Texts(Read("checkboxes", null, new FieldAnswers(["a"], [Part("other", "My own")]))), Is.EqualTo(new[] { "a" }), "a field without Other ignores it");
            Assert.That(Read("checkboxes", Other, new FieldAnswers(["a", "a"])).IsUnreadable, Is.True, "an option given twice");
        });
    }

    [Test]
    public void AGrid_GivesEachRowItsColumns()
    {
        var read = Read("grid", null, new FieldAnswers(["ignored"], [Part(First.ToString("D"), "good"), Part(Second.ToString("D"), "bad"), Part(Guid.NewGuid().ToString("D"), "ignored")]), rows: [First, Second]);

        Assert.Multiple(() =>
        {
            Assert.That(read.Values.Select(value => (value.Text, value.Row)), Is.EqualTo(new[] { ("good", (Guid?)First), ("bad", Second) }));
            Assert.That(GridFieldType.RowPart(First), Is.EqualTo("11111111-1111-1111-1111-111111111111"));
        });
    }

    [Test]
    public void AGrid_TakesSeveralColumnsOfARow_OnlyWhenItTakesSeveral()
    {
        var answers = new FieldAnswers([], [Part(First.ToString("D"), "good", "bad")]);

        Assert.Multiple(() =>
        {
            Assert.That(Read("grid", null, answers, rows: [First]).IsUnreadable, Is.True);
            Assert.That(Texts(Read("grid", """{"multiple":true}""", answers, rows: [First])), Is.EqualTo(new[] { "good", "bad" }));
            Assert.That(Read("grid", """{"multiple":true}""", new FieldAnswers([], [Part(First.ToString("D"), "good", "good")]), rows: [First]).IsUnreadable, Is.True, "a column given twice");
        });
    }

    [Test]
    public void ARanking_TakesTheOptionsInTheirOrder_OrByThePositionsGiven()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Texts(Read("ranking", null, new FieldAnswers(["c", "a", "b"]))), Is.EqualTo(new[] { "c", "a", "b" }));
            Assert.That(Texts(Read("ranking", null, new FieldAnswers([], [Part("a", "3"), Part("b", "1"), Part("c", "2")]))), Is.EqualTo(new[] { "b", "c", "a" }), "a form without JavaScript");
            Assert.That(Texts(Read("ranking", null, new FieldAnswers([], [Part("a", "10"), Part("b", "2")]))), Is.EqualTo(new[] { "b", "a" }), "positions as numbers, gaps allowed");
        });
    }

    [Test]
    public void ARanking_ReadsAnOptionOrAPositionGivenTwice_ForItsCheckToRefuse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Texts(Read("ranking", null, new FieldAnswers(["a", "b", "a"]))), Is.EqualTo(new[] { "a", "b", "a" }));
            Assert.That(Texts(Read("ranking", null, new FieldAnswers([], [Part("c", "1"), Part("a", "2"), Part("b", "1")]))), Is.EqualTo(new[] { "b", "c", "a" }), "options in one position in the order of their values");
        });
    }

    [TestCase("0", "1")]
    [TestCase("-1", "1")]
    [TestCase("x", "1")]
    [TestCase("+1", "2")]
    [TestCase("1 ", "2")]
    public void ARanking_CannotReadPositionsThatAreNone(string a, string b) =>
        Assert.That(Read("ranking", null, new FieldAnswers([], [Part("a", a), Part("b", b)])).IsUnreadable, Is.True);

    [Test]
    public void ARanking_CannotReadBothForms() =>
        Assert.That(Read("ranking", null, new FieldAnswers(["a"], [Part("b", "1")])).IsUnreadable, Is.True);

    [Test]
    public void AConsent_ReadsATick_AndStoresTheWordingsHash()
    {
        var given = Read("consent", null, new FieldAnswers(["true"], [Part("wording", Hash)]));

        Assert.Multiple(() =>
        {
            Assert.That(given.Values.Select(value => (value.Text, value.StoredText)), Is.EqualTo(new[] { ("true", (string?)Hash) }), "rules compare a ticked box");
            Assert.That(Read("consent", null, new FieldAnswers([], [Part("wording", Hash)])).Values, Is.Empty, "not given");
            Assert.That(Read("consent", null, new FieldAnswers(["yes"], [Part("wording", Hash)])).Values, Is.Empty, "anything but a tick leaves it not given");
            Assert.That(Read("consent", null, new FieldAnswers(["true", "true"], [Part("wording", Hash)])).IsUnreadable, Is.True, "two ticks");
        });
    }

    [TestCase(null)]
    [TestCase("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [TestCase("0123456789abcdef")]
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public void AConsent_CannotReadATick_WithoutTheHashOfAWording(string? hash)
    {
        KeyValuePair<string, IEnumerable<string>>[] parts = hash is null ? [] : [Part("wording", hash)];

        Assert.That(Read("consent", null, new FieldAnswers(["true"], parts)).IsUnreadable, Is.True);
    }

    [Test]
    public void AName_IsOneObjectOfThePartsTheFieldAsksFor_InTheirOrder()
    {
        var answers = new FieldAnswers([], [Part("last", "Lovelace"), Part("title", "Dr"), Part("first", "Ada"), Part("nickname", "ignored")]);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(Read("name", null, answers)), Is.EqualTo(new[] { """{"first":"Ada","last":"Lovelace"}""" }), "the first and last name by default");
            Assert.That(Texts(Read("name", """{"parts":["last","middle","first","title"]}""", answers)), Is.EqualTo(new[] { """{"title":"Dr","first":"Ada","last":"Lovelace"}""" }), "an empty part left out");
            Assert.That(Read("name", null, new FieldAnswers([], [Part("title", "Dr")])).Values, Is.Empty, "a part the field does not ask for");
            Assert.That(Read("name", null, new FieldAnswers([], [Part("first", "Ada", "Augusta")])).IsUnreadable, Is.True, "a part given twice");
        });
    }

    [Test]
    public void AnAddress_AlwaysTakesTheStreetThePostcodeAndTheCity()
    {
        var answers = new FieldAnswers([], [Part("country", "CH"), Part("city", "Bern"), Part("street2", "Floor 3"), Part("postcode", "3000"), Part("street", "Main Street 1")]);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(Read("address", null, answers)), Is.EqualTo(new[] { """{"street":"Main Street 1","postcode":"3000","city":"Bern"}""" }));
            Assert.That(Texts(Read("address", """{"parts":["country","street2"]}""", answers)), Is.EqualTo(new[] { """{"street":"Main Street 1","street2":"Floor 3","postcode":"3000","city":"Bern","country":"CH"}""" }));
        });
    }

    [Test]
    public void APartsText_IsWrittenAsJson()
    {
        var quoted = "Say \"hi\" " + (char)0x5C;

        Assert.That(Texts(Read("name", null, new FieldAnswers([], [Part("first", quoted)]))), Is.EqualTo(new[] { "{\"first\":\"Say \\\"hi\\\" \\\\\"}" }));
    }

    private static string[] Texts(FieldRead read) => [.. read.Values.Select(value => value.Text)];

    private static FieldRead Read(string type, string? settings, FieldAnswers answers, Guid[]? rows = null) =>
        FieldValues.Read(Type(type), Field(type, settings, rows ?? []), answers);

    private static IFormBuilderFieldType Type(string alias) => TestFieldTypes.Registry.FindType(alias)!;

    private static FormField Field(string type, string? settings, int rows) =>
        Field(type, settings, [.. Enumerable.Range(1, rows).Select(_ => Guid.NewGuid())]);

    private static FormField Field(string type, string? settings, Guid[] rows) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = type,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
        Rows = [.. rows.Select(row => new GridRow { Key = row })],
    };

    private static KeyValuePair<string, IEnumerable<string>> Part(string name, params string[] texts) => new(name, texts);
}
