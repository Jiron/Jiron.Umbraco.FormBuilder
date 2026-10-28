using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// How the entries endpoint reads its body (ADR-0088): every answer shape by alias, parts as <c>alias.part</c>, a
/// repeatable group's rows, consents as their wording part, query values and context values, each into the submission
/// the pipeline takes; and where a body cannot be read, by the path of the value, every one named once.
/// </summary>
public sealed class EntryBodiesTests
{
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000088");
    private static readonly Guid Submission = Guid.Parse("e0000000-0000-4000-8000-000000000088");
    private static readonly Guid Page = Guid.Parse("c0000000-0000-4000-8000-000000000088");

    [Test]
    public void ABody_IsReadIntoTheSubmission_WithEveryShapeOfAnswer()
    {
        var submission = Read($$"""
            {"submissionId":"{{Submission}}","revision":3,"culture":"de-CH","contentKey":"{{Page}}",
             "values":{"name":"Ada","amount":1.5e3,"agree":true,"off":false,"none":null,"topics":["a","b"],
                       "address.city":"Bern","address.country":"CH",
                       "guests":[{"guestName":"Grace","ticket":["t1"]},{"fullName.first":"Alan"}]},
             "consents":{"privacy":"0123abcd","skipped":null},
             "query":{"source":"news","tags":["x","y"],"gone":null},
             "context":{"campaign":"autumn","none":null},
             "challenge":"a challenge","captchaToken":"a token"}
            """, out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(invalid, Is.Empty);
            Assert.That((submission!.Form, submission.Revision, submission.SubmissionId, submission.Culture, submission.ContentKey), Is.EqualTo((Form, 3, (Guid?)Submission, "de-CH", (Guid?)Page)));
            Assert.That(submission.Fields["name"].Values, Is.EqualTo(new[] { "Ada" }));
            Assert.That(submission.Fields["amount"].Values, Is.EqualTo(new[] { "1.5e3" }), "a number as it is written");
            Assert.That(submission.Fields["agree"].Values, Is.EqualTo(new[] { "true" }));
            Assert.That(submission.Fields["off"].Values, Is.EqualTo(new[] { "false" }));
            Assert.That(submission.Fields.TryGetValue("none", out var none) ? none.Values : [], Is.Empty, "null is no answer");
            Assert.That(submission.Fields["topics"].Values, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(submission.Fields["address"].Parts!.ToDictionary(part => part.Key, part => part.Value.ToArray()), Is.EqualTo(new Dictionary<string, string?[]> { ["city"] = ["Bern"], ["country"] = ["CH"] }));
            Assert.That(submission.Fields["privacy"].Parts!["wording"], Is.EqualTo(new[] { "0123abcd" }), "a ticked consent's hash as its wording part");
            Assert.That(submission.Fields.ContainsKey("skipped"), Is.False);
            Assert.That(submission.Fields.ContainsKey("guests"), Is.False, "a repeatable group's answers are its rows");
            Assert.That(submission.Rows["guests"], Has.Count.EqualTo(2));
            Assert.That(submission.Rows["guests"][0]["guestName"].Values, Is.EqualTo(new[] { "Grace" }));
            Assert.That(submission.Rows["guests"][0]["ticket"].Values, Is.EqualTo(new[] { "t1" }));
            Assert.That(submission.Rows["guests"][1]["fullName"].Parts!["first"], Is.EqualTo(new[] { "Alan" }));
            Assert.That(submission.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()), Is.EqualTo(new Dictionary<string, string[]> { ["source"] = ["news"], ["tags"] = ["x", "y"] }));
            Assert.That(submission.Context, Is.EqualTo(new Dictionary<string, string> { ["campaign"] = "autumn" }));
            Assert.That(submission.Tokens, Is.Null, "the transport adds its own");
        });
    }

    [Test]
    public void TheOptionalMembers_MayBeLeftOutOrNull()
    {
        var submission = Read("""{"revision":1,"submissionId":null,"culture":null,"contentKey":null,"values":null,"consents":null,"query":null,"context":null,"challenge":null,"captchaToken":null}""", out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(invalid, Is.Empty);
            Assert.That((submission!.SubmissionId, submission.Culture, submission.ContentKey), Is.EqualTo(((Guid?)null, (string?)null, (Guid?)null)));
            Assert.That((submission.Fields.Count, submission.Rows.Count, submission.Query.Count, submission.Context.Count), Is.EqualTo((0, 0, 0, 0)));
        });
    }

    [TestCase("""{"revision":2147483648}""", "revision")]
    [TestCase("""{"revision":1.5}""", "revision")]
    [TestCase("""{"revision":null}""", "revision")]
    [TestCase("""{"revision":1,"submissionId":"00000000-0000-0000-0000-00000000000"}""", "submissionId")]
    [TestCase("""{"revision":1,"contentKey":7}""", "contentKey")]
    [TestCase("""{"revision":1,"culture":"en_US"}""", "culture")]
    [TestCase("""{"revision":1,"values":[]}""", "values")]
    [TestCase("""{"revision":1,"values":{"name":"a","name":"b"}}""", "values.name")]
    [TestCase("""{"revision":1,"values":{"name":[["a"]]}}""", "values.name")]
    [TestCase("""{"revision":1,"values":{"name":[null]}}""", "values.name")]
    [TestCase("""{"revision":1,"values":{"g.part":[{"c":"a"}]}}""", "values.g.part")]
    [TestCase("""{"revision":1,"values":{"g":[{"c":{"x":1}}]}}""", "values.g[0].c")]
    [TestCase("""{"revision":1,"values":{"g":[{"c":"a"},{"c":"a","c":"b"}]}}""", "values.g[1].c")]
    [TestCase("""{"revision":1,"values":{"":"a"}}""", "values.")]
    [TestCase("""{"revision":1,"values":{"name.":"a"}}""", "values.name.")]
    [TestCase("""{"revision":1,"values":{".first":"a"}}""", "values..first")]
    [TestCase("""{"revision":1,"values":{"g":[{"c.":"a"}]}}""", "values.g[0].c.")]
    [TestCase("""{"revision":1,"values":{"g":[{"c":"a"}],"g.x":"y"}}""", "values.g.x")]
    [TestCase("""{"revision":1,"values":{"g.x":"y","g":[{"c":"a"}]}}""", "values.g")]
    [TestCase("""{"revision":1,"values":{"privacy.wording":"0123abcd"},"consents":{"privacy":"0123abcd"}}""", "consents.privacy")]
    [TestCase("""{"revision":1,"consents":[]}""", "consents")]
    [TestCase("""{"revision":1,"consents":{"privacy":1}}""", "consents.privacy")]
    [TestCase("""{"revision":1,"consents":{"privacy.x":"0123abcd"}}""", "consents.privacy.x")]
    [TestCase("""{"revision":1,"query":{"a":{"b":1}}}""", "query.a")]
    [TestCase("""{"revision":1,"query":"a"}""", "query")]
    [TestCase("""{"revision":1,"context":{"a":1}}""", "context.a")]
    [TestCase("""{"revision":1,"context":["a"]}""", "context")]
    [TestCase("""{"revision":1,"challenge":1}""", "challenge")]
    [TestCase("""{"revision":1,"captchaToken":true}""", "captchaToken")]
    [TestCase("""{"revision":1,"uploads":[]}""", "uploads")]
    [TestCase("""{"revision":1} {}""", "body")]
    [TestCase(""" "text" """, "body")]
    public void AValueThatCannotBeRead_IsNamedByItsPath(string body, string path)
    {
        var submission = Read(body, out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(submission, Is.Null);
            Assert.That(invalid, Is.EqualTo(new[] { path }));
        });
    }

    [Test]
    public void EveryValueThatCannotBeRead_IsNamedOnce_AndTheLimitsOfTheChallengeAndTheTokenHold()
    {
        var submission = Read($$$"""
            {"values":{"a":{},"a.b":{}},"challenge":"{{{new string('c', EntryBodies.MaxChallengeLength + 1)}}}",
             "captchaToken":"{{{"JFB_FAKE" + new string('t', EntryBodies.MaxCaptchaTokenLength - 7)}}}","context":{"x":1,"x":2}}
            """, out var invalid);
        var longest = Read($$$"""
            {"revision":1,"challenge":"{{{new string('c', EntryBodies.MaxChallengeLength)}}}","captchaToken":"{{{"JFB_FAKE" + new string('t', EntryBodies.MaxCaptchaTokenLength - 8)}}}"}
            """, out var none);

        Assert.Multiple(() =>
        {
            Assert.That(submission, Is.Null);
            Assert.That(invalid, Is.EquivalentTo(new[] { "values.a", "values.a.b", "challenge", "captchaToken", "context.x", "revision" }));
            Assert.That(longest, Is.Not.Null);
            Assert.That(none, Is.Empty);
        });
    }

    [Test]
    public void BytesThatAreNotUtf8_AndALoneSurrogate_AreNamedWhereTheyAre()
    {
        var backslash = (char)92;
        var invalidBytes = new List<string>();
        var bytes = EntryBodies.Read([.. Encoding.UTF8.GetBytes("{\"revision\":1,\"values\":{\"name\":\""), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("\"}}")], Form, invalidBytes);
        var surrogateValue = Read($$$"""{"revision":1,"values":{"name":"{{{backslash}}}uDFFF"}}""", out var inValue);
        var surrogateRow = Read($$$"""{"revision":1,"values":{"g":[{"c":"{{{backslash}}}uD800x"}]}}""", out var inRow);
        var surrogateName = Read($$$"""{"revision":1,"query":{"{{{backslash}}}uD800":"x"}}""", out var inName);
        var surrogateKeys = Read($$$"""{"revision":1,"submissionId":"{{{backslash}}}uD800","contentKey":"{{{backslash}}}uDC00"}""", out var inKeys);
        var pair = Read($$$"""{"revision":1,"values":{"name":"{{{backslash}}}uD83D{{{backslash}}}uDE00"}}""", out var none);

        Assert.Multiple(() =>
        {
            Assert.That((bytes, invalidBytes), Is.EqualTo(((SubmissionRequest?)null, new List<string> { "body" })));
            Assert.That((surrogateValue, inValue), Is.EqualTo(((SubmissionRequest?)null, new List<string> { "values.name" })));
            Assert.That((surrogateRow, inRow), Is.EqualTo(((SubmissionRequest?)null, new List<string> { "values.g[0].c" })));
            Assert.That((surrogateName, inName), Is.EqualTo(((SubmissionRequest?)null, new List<string> { "query" })));
            Assert.That((surrogateKeys, inKeys), Is.EqualTo(((SubmissionRequest?)null, new List<string> { "submissionId", "contentKey" })), "a key with a lone surrogate is no UUID");
            Assert.That(none, Is.Empty, "a surrogate pair is one character");
            Assert.That(pair!.Fields["name"].Values[0], Is.EqualTo(char.ConvertFromUtf32(0x1F600)));
        });
    }

    private static SubmissionRequest? Read(string json, out List<string> invalid)
    {
        invalid = [];
        return EntryBodies.Read(Encoding.UTF8.GetBytes(json), Form, invalid);
    }
}
