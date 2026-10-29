using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The answers of a post without JavaScript (ADR-0100), read by the names the form's controls carry: a field's texts and
/// parts, a repeatable group's rows as many as the page showed, a consent's wording, the context and query values, a
/// switch left off as <c>no</c>, every CRLF as LF; names of no field, rows past the count and malformed names are left
/// out, and a field named by an alias it had is read by that name.
/// </summary>
public sealed class FormPostReaderTests
{
    private static readonly string Crlf = new([(char)13, (char)10]);
    private static readonly string Lf = ((char)10).ToString();

    private static readonly FormField FullName = Field(K(1), "fullName", "name");
    private static readonly FormField Story = Field(K(2), "story", "textarea");
    private static readonly FormField Updates = Field(K(3), "updates", "yesno", """{"style":"switch"}""");
    private static readonly FormField Answer = Field(K(4), "answer", "yesno");
    private static readonly FormField Terms = Field(K(5), "terms", "consent", """{"text":{"":"<p>I agree.</p>"}}""");
    private static readonly FormField Guest = Field(K(7), "guest", "text");
    private static readonly FormField Vegan = Field(K(8), "vegan", "yesno", """{"style":"switch"}""");
    private static readonly FormField Agree = Field(K(9), "agree", "consent", """{"text":{"":"<p>Me too.</p>"}}""");
    private static readonly FormField Guests = Field(K(6), "guests", "repeater") with { Fields = [Guest, Vegan, Agree] };
    private static readonly FormField Topic = Field(K(10), "topic", "text") with { FormerAliases = ["subject", "story"] };

    private static readonly FormDefinition Definition = new()
    {
        Key = K(0),
        Pages =
        [
            new FormPage { Key = K(20), Groups = [new FormGroup { Key = K(21), Fields = [FullName, Story, Updates, Answer, Terms] }] },
            new FormPage { Key = K(22), Groups = [new FormGroup { Key = K(23), Fields = [Guests, Topic] }] },
        ],
    };

    [Test]
    public void AFieldsTextsAndParts_ReachThePipelineByTheirNames_EveryCrlfAsLf_AndOtherNamesAreLeftOut()
    {
        var reader = Read(
            ("fullName.first", ["Ada"]),
            ("fullName.last", ["Lovelace"]),
            ("story", ["One" + Crlf + "two" + Crlf + Crlf + "three"]),
            ("updates", ["yes"]),
            ("answer", ["no", "yes"]),
            ("jfb-form", [K(0).ToString("D")]),
            ("jfb-unknown", ["x"]),
            ("ufprt", ["token"]),
            ("newsletter", ["yes"]),
            ("guest", ["Bob"]),
            ("fullName.", ["dot"]),
            (".first", ["nothing"]));

        var (fields, rows) = reader.Posted();

        Assert.Multiple(() =>
        {
            Assert.That(fields.Keys, Is.EquivalentTo(new[] { "fullName", "story", "updates", "answer" }), "the form's fields alone; a child outside its group is no field");
            Assert.That(fields["fullName"].Values, Is.EqualTo(new[] { "dot" }), "a name with nothing after its dot is the field's own text");
            Assert.That(fields["fullName"].Parts, Is.EqualTo(new Dictionary<string, IReadOnlyList<string?>> { ["first"] = ["Ada"], ["last"] = ["Lovelace"] }));
            Assert.That(fields["story"].Values, Is.EqualTo(new[] { "One" + Lf + "two" + Lf + Lf + "three" }), "line breaks as the runtime sends them");
            Assert.That(fields["story"].Parts, Is.Null);
            Assert.That(fields["answer"].Values, Is.EqualTo(new[] { "no", "yes" }), "every value, for the pipeline to judge");
            Assert.That(rows, Is.Empty);
            Assert.That(reader.Context, Is.Empty);
            Assert.That(reader.Query, Is.Empty);
        });
    }

    [Test]
    public void ARepeatableGroupsRows_AreAsManyAsThePageShowed_EmptyRowsKeepingTheirPlace()
    {
        var reader = Read(
            ("jfb-rows-guests", ["3"]),
            ("guests[0].guest", ["Ada"]),
            ("guests[2].guest", ["Sam" + Crlf + "Lin"]),
            ("guests[3].guest", ["past the count"]),
            ("guests[1].unknown", ["no child"]),
            ("guests[x].guest", ["no row"]),
            ("guests[1]guest", ["no dot"]),
            ("guests[1].", ["no child's name"]),
            ("[1].guest", ["no group"]),
            ("guests[1.guest", ["unclosed"]),
            ("topic[0].guest", ["no group of that name"]));

        var (_, rows) = reader.Posted();
        var (_, shown) = reader.Shown();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Keys, Is.EqualTo(new[] { "guests" }));
            Assert.That(rows["guests"].Select(row => row.TryGetValue("guest", out var guest) ? guest.Values.Single() : null), Is.EqualTo(new[] { "Ada", null, "Sam" + Lf + "Lin" }), "the rows the count names, the second one whose box posted nothing");
            Assert.That(rows["guests"].Select(row => row["vegan"].Values.Single()), Is.EqualTo(new[] { "no", "no", "no" }), "a switch left off in each row");
            Assert.That(shown[Guests.Key].Select(row => row.ContainsKey(Guest.Key)), Is.EqualTo(new[] { true, false, true }));
        });
    }

    [Test]
    public void WithoutACount_ThePostedRowsReachTheHighestNamed_AndNoCountOrRowPassesTheMost()
    {
        var most = RepeaterFieldType.MostRows;
        var named = Read(("guests[1].guest", ["Sam"]));
        var counted = Read(("jfb-rows-guests", [(most + 50).ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        var past = Read(("guests[" + most.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].guest", ["past the most"]));
        var unreadable = Read(("jfb-rows-guests", ["two"]), ("guests[0].guest", ["Ada"]));
        var negative = Read(("jfb-rows-guests", ["-1"]));
        var none = Read();

        Assert.Multiple(() =>
        {
            Assert.That(named.Posted().Rows["guests"], Has.Count.EqualTo(2), "the first row empty, the second Sam's");
            Assert.That(counted.Posted().Rows["guests"], Has.Count.EqualTo(most));
            Assert.That(past.Posted().Rows.ContainsKey("guests"), Is.False, "a row past the most names no row");
            Assert.That(unreadable.Posted().Rows["guests"], Has.Count.EqualTo(1), "a count that is no number counts the rows posted");
            Assert.That(negative.Posted().Rows.ContainsKey("guests"), Is.False);
            Assert.That(none.Posted().Rows, Is.Empty, "a group without rows posts none");
            Assert.That(none.Shown().Rows[Guests.Key], Is.Empty);
        });
    }

    [Test]
    public void ASwitchLeftOff_IsNo_AButtonsYesOrNoLeftOutIsNoAnswer_AndASwitchOnStaysYes()
    {
        var off = Read();
        var on = Read(("updates", ["yes"]));
        var empty = Read(("updates", [string.Empty]));

        Assert.Multiple(() =>
        {
            Assert.That(off.Posted().Fields["updates"].Values, Is.EqualTo(new[] { YesNoFieldType.No }), "as the runtime sends a switch left off");
            Assert.That(off.Posted().Fields.ContainsKey("answer"), Is.False, "two buttons left alone are no answer");
            Assert.That(off.Shown().Fields[Updates.Key].Values, Is.EqualTo(new[] { "no" }));
            Assert.That(on.Posted().Fields["updates"].Values, Is.EqualTo(new[] { "yes" }));
            Assert.That(empty.Posted().Fields["updates"].Values, Is.EqualTo(new[] { "no" }), "an empty value is no answer either");
        });
    }

    [Test]
    public void AConsentsWording_IsItsPart_TheTickedOnesAreListed_AndOneCanBeUnticked()
    {
        var reader = Read(
            ("terms", [FieldAnswerDefaults.Ticked]),
            ("jfb-consent-terms", ["hash-terms"]),
            ("jfb-rows-guests", ["2"]),
            ("guests[1].agree", [FieldAnswerDefaults.Ticked]),
            ("jfb-consent-guests[1].agree", ["hash-agree"]),
            ("jfb-consent-guests[0].agree", ["hash-unticked"]),
            ("jfb-consent-terms.part", ["no part of a part"]),
            ("jfb-consent-unknown", ["no field"]));

        var ticked = reader.Ticked().ToList();
        var before = reader.Posted();
        reader.Untick(Terms.Key, null);
        reader.Untick(Agree.Key, 1);
        reader.Untick(Agree.Key, 5);
        var after = reader.Posted();

        Assert.Multiple(() =>
        {
            Assert.That(before.Fields["terms"].Parts, Is.EqualTo(new Dictionary<string, IReadOnlyList<string?>> { [ConsentFieldType.WordingPart] = ["hash-terms"] }));
            Assert.That(ticked, Is.EqualTo(new (Guid, int?, string?)[] { (Terms.Key, null, "hash-terms"), (Agree.Key, 1, "hash-agree") }), "the ticked ones, a row's with its row");
            Assert.That(before.Rows["guests"][0]["agree"].Values, Is.Empty, "an unticked consent's wording alone");
            Assert.That(after.Fields["terms"].Values, Is.Empty, "unticked, its wording kept");
            Assert.That(after.Fields["terms"].Parts![ConsentFieldType.WordingPart], Is.EqualTo(new[] { "hash-terms" }));
            Assert.That(after.Rows["guests"][1]["agree"].Values, Is.Empty);
            Assert.That(reader.Ticked(), Is.Empty);
        });
    }

    [Test]
    public void APartPostedUnderTwoNames_HoldsBothTexts_InTheOrderPosted()
    {
        var reader = Read(("jfb-consent-terms", ["hash"]), ("terms.wording", ["typed"]), ("terms", [FieldAnswerDefaults.Ticked]));

        Assert.Multiple(() =>
        {
            Assert.That(reader.Posted().Fields["terms"].Parts![ConsentFieldType.WordingPart], Is.EqualTo(new[] { "hash", "typed" }), "for the pipeline to judge");
            Assert.That(reader.Ticked().Single().Hash, Is.EqualTo("hash"), "the first is the wording's hash");
        });
    }

    [Test]
    public void AFieldNamedByAnAliasItHad_IsReadByThatName_AndAnAliasAnotherFieldHasNowIsThatFields()
    {
        var former = Read(("subject", ["By its old name"]));
        var both = Read(("subject", ["first"]), ("topic", ["second"]));
        var taken = Read(("story", ["Story's own"]));

        Assert.Multiple(() =>
        {
            Assert.That(former.Posted().Fields.Keys, Is.EquivalentTo(new[] { "subject", "updates" }), "the name the page of an older revision gave it, for the pipeline to read");
            Assert.That(former.Shown().Fields[Topic.Key].Values, Is.EqualTo(new[] { "By its old name" }), "rendered again by its key");
            Assert.That(both.Posted().Fields["subject"].Values, Is.EqualTo(new[] { "first" }), "the first name of the field the post holds");
            Assert.That(both.Posted().Fields.ContainsKey("topic"), Is.False);
            Assert.That(taken.Shown().Fields.ContainsKey(Topic.Key), Is.False, "another field's alias now is that field's");
            Assert.That(taken.Shown().Fields[Story.Key].Values, Is.EqualTo(new[] { "Story's own" }));
        });
    }

    [Test]
    public void TheContextAndQueryValues_AreReadByTheirNames_AndShownAnswersLeaveEmptyTextsOut()
    {
        var reader = Read(
            ("jfb-context-campaign", ["spring"]),
            ("jfb-context-twice", ["one", "two"]),
            ("jfb-query-utm", ["a", "b"]),
            ("fullName.first", [string.Empty]),
            ("fullName.last", ["Lovelace"]),
            ("fullName. ", ["a blank part"]),
            ("story", [string.Empty]));

        var (fields, _) = reader.Shown();

        Assert.Multiple(() =>
        {
            Assert.That(reader.Context, Is.EqualTo(new Dictionary<string, string> { ["campaign"] = "spring", ["twice"] = "two" }), "the last of several");
            Assert.That(reader.Query, Is.EqualTo(new Dictionary<string, IReadOnlyList<string>> { ["utm"] = ["a", "b"] }));
            Assert.That(fields[FullName.Key].Parts.Keys, Is.EqualTo(new[] { "last" }), "a part posted empty, or of a blank name, is no answer to show");
            Assert.That(reader.Posted().Fields["fullName"].Parts!.Keys, Is.EquivalentTo(new[] { "first", "last", " " }), "the pipeline judges every part");
            Assert.That(fields.ContainsKey(Story.Key), Is.False, "nor is a text posted empty");
            Assert.That(reader.Posted().Fields["story"].Values, Is.EqualTo(new[] { string.Empty }), "the pipeline reads it as the runtime's empty text");
        });
    }

    [Test]
    public void ReadingWithoutAPostAFormOrTypes_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FormPostReader.Read(null!, Definition, TestFieldTypes.Registry), Throws.ArgumentNullException);
            Assert.That(() => FormPostReader.Read(new FormCollection([]), null!, TestFieldTypes.Registry), Throws.ArgumentNullException);
            Assert.That(() => FormPostReader.Read(new FormCollection([]), Definition, null!), Throws.ArgumentNullException);
        });
    }

    private static FormPostReader Read(params (string Name, string[] Values)[] posted) =>
        FormPostReader.Read(new FormCollection(posted.ToDictionary(pair => pair.Name, pair => new StringValues(pair.Values), StringComparer.Ordinal)), Definition, TestFieldTypes.Registry);

    private static FormField Field(Guid key, string alias, string type, string settings = "{}") => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
        Settings = settings == "{}" ? null : SettingsObject.Parse(settings),
    };

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-000000100{number:x3}");
}
