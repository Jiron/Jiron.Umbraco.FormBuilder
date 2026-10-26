using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// <c>entry.answers</c> (ADR-0014, ADR-0066): every answered field the rules did not hide, in the order of the form, as
/// <c>Label: answer</c> lines, and a table in HTML; and what each place leaves out or masks.
/// </summary>
public sealed class EntryAnswersTests
{
    private static readonly string Bullets = new((char)0x2022, 3);

    /// <summary>A form with a field of each kind the list treats on its own, and answers for all of them.</summary>
    private static (FormDefinition Form, MergeTagAnswer[] Answers) Contact()
    {
        var name = Field("text", "name", "Your <name>");
        var message = Field("textarea", "message", "Message");
        var fruit = Field("checkboxes", "fruit", "Fruit") with { Options = Options(("a", "Apple"), ("b", "Banana")) };
        var unanswered = Field("text", "unanswered", "Unanswered");
        var campaign = Field("hidden", "campaign", "Campaign");
        var total = Field("calculation", "total", "Total", """{"formula":"1","visible":false}""");
        var price = Field("calculation", "price", "Price", """{"formula":"1","format":{"suffix":{"":"CHF"}}}""");
        var heading = Field("heading", "heading");
        var childName = Field("text", "childName", "Name");
        var childAge = Field("number", "childAge", "Age");
        var children = Field("repeater", "children", "Children") with { Fields = [childName, childAge] };
        var form = Form(name, message, fruit, unanswered, campaign, total, price, heading, children);
        MergeTagAnswer[] answers =
        [
            Answer(name, "Anna & Ben"),
            Answer(message, "Line 1\nLine <2>"),
            Answer(fruit, "a", "b"),
            Answer(campaign, "spring"),
            Answer(total, "3"),
            Answer(price, "12.5"),
            InRow(childName, 0, "Cleo"),
            InRow(childAge, 0, "7"),
            InRow(childName, 1, "Dan"),
        ];
        return (form, answers);
    }

    [Test]
    public async Task TheAnswers_InText_AreLabelAndAnswerLines_ARepeatableGroupsRowsNumbered()
    {
        var (form, answers) = Contact();

        var rendered = await MergeTagTestSite.Create().RenderAsync("{{ entry.answers }}", Scope(form, Answers(answers)));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(
                "Your <name>: Anna & Ben\nMessage: Line 1\nLine <2>\nFruit: Apple, Banana\nPrice: 12.50 CHF\nChildren:\n1. Name: Cleo; Age: 7\n2. Name: Dan"),
                "no unanswered field, hidden value, unseen calculation or content");
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task TheAnswers_InHtml_AreATable_EveryTextEscaped_LineBreaksKept()
    {
        var (form, answers) = Contact();

        var rendered = await MergeTagTestSite.Create().RenderAsync("<div>{{ entry.answers }}</div>", Scope(form, Answers(answers)), MergeTagPlace.Mail, MergeTagContext.Html);

        Assert.That(rendered.Text, Is.EqualTo(
            "<div><table class=\"jfb-answers\"><tbody>\n"
            + "<tr><th scope=\"row\">Your &lt;name&gt;</th><td>Anna &amp; Ben</td></tr>\n"
            + "<tr><th scope=\"row\">Message</th><td>Line 1<br>Line &lt;2&gt;</td></tr>\n"
            + "<tr><th scope=\"row\">Fruit</th><td>Apple, Banana</td></tr>\n"
            + "<tr><th scope=\"row\">Price</th><td>12.50 CHF</td></tr>\n"
            + "<tr><th scope=\"row\">Children</th><td><ol><li>Name: Cleo; Age: 7</li><li>Name: Dan</li></ol></td></tr>\n"
            + "</tbody></table></div>"));
    }

    [Test]
    public async Task TheAnswers_ThroughAFilter_AreTheTextEscaped_NotTheTable()
    {
        var (form, answers) = Contact();
        var site = MergeTagTestSite.Create();

        var truncated = await site.RenderAsync("{{ entry.answers | truncate: 19 }}", Scope(form, Answers(answers)), MergeTagPlace.Mail, MergeTagContext.Html);
        var html = await site.RenderAsync("{{ entry.answers | html }}", Scope(form, Answers(answers)), MergeTagPlace.Mail, MergeTagContext.Html);
        var json = await site.RenderAsync("{\"answers\": {{ entry.answers }}}", Scope(form, Answers(answers)), MergeTagPlace.Action, MergeTagContext.Json);

        Assert.Multiple(() =>
        {
            Assert.That(truncated.Text, Is.EqualTo("Your &lt;name&gt;: Anna &amp;" + (char)0x2026));
            Assert.That(html.Text, Does.StartWith("Your &lt;name&gt;: Anna &amp; Ben<br>Message: Line 1<br>Line &lt;2&gt;<br>"));
            Assert.That(json.Text, Does.StartWith("{\"answers\": \"Your " + Escaped('<') + "name" + Escaped('>') + ": Anna " + Escaped('&') + " Ben\\nMessage: "));
        });
    }

    [Test]
    public async Task TheAnswers_LeaveOutWhatTheRulesHid_AndAreEmptyWithoutAny()
    {
        var (form, answers) = Contact();
        var fields = form.Pages[0].Groups[0].Fields;
        var hidden = new MergeTagAnswers(answers, [fields[1].Key, fields[2].Key, fields[6].Key, fields[8].Key]);
        var site = MergeTagTestSite.Create();

        var some = await site.RenderAsync("{{ entry.answers }}", Scope(form, hidden));
        var none = await site.RenderAsync("[{{ entry.answers }}]", Scope(form, MergeTagAnswers.None), MergeTagPlace.Outcome, MergeTagContext.Html);

        Assert.Multiple(() =>
        {
            Assert.That(some.Text, Is.EqualTo("Your <name>: Anna & Ben"));
            Assert.That(none.Text, Is.EqualTo("[]"));
        });
    }

    [Test]
    public async Task Mail_LeavesOutTheFieldsNotForMail_TheOutcomeListsThem()
    {
        var name = Field("text", "name", "Name");
        var note = Field("text", "note", "Internal note") with { IncludeInEmails = false };
        var childName = Field("text", "childName", "Name");
        var children = Field("repeater", "children", "Children") with { Fields = [childName], IncludeInEmails = false };
        var form = Form(name, note, children);
        var answers = Answers(Answer(name, "Anna"), Answer(note, "VIP"), InRow(childName, 0, "Ben"));
        var site = MergeTagTestSite.Create();

        var mail = await site.RenderAsync("{{ entry.answers }} / {{ field.note }}", Scope(form, answers), MergeTagPlace.Mail);
        var action = await site.RenderAsync("{{ entry.answers }}", Scope(form, answers), MergeTagPlace.Action);

        Assert.Multiple(() =>
        {
            Assert.That(mail.Text, Is.EqualTo("Name: Anna / VIP"), "a tag that names the field still shows it");
            Assert.That(action.Text, Is.EqualTo("Name: Anna\nInternal note: VIP\nChildren:\n1. Name: Ben"));
        });
    }

    /// <summary>
    /// What a sensitive field shows in each place (ADR-0014): its tag and its line in <c>entry.answers</c>, the field
    /// named by the action to include it, or not; a place that takes neither source renders the tags as they are written.
    /// </summary>
    private static IEnumerable<TestCaseData> Places()
    {
        var bullets = new string((char)0x2022, 3);
        var encoded = string.Concat(Enumerable.Repeat("%E2%80%A2", 3));
        TestCaseData Case(MergeTagPlace place, MergeTagContext context, string? template, bool included, string expected) =>
            new TestCaseData(place, context, template, included, expected).SetName($"{{m}}({place}, {(included ? "named to include" : "not named")})");

        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Text, "{{ field.secret }}|{{ field.child }}", false, $"{bullets}|{bullets}");
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Text, "{{ field.secret }}", true, bullets);
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Text, "{{ entry.answers }}", false, "{{ entry.answers }}");
        yield return Case(MergeTagPlace.ConsentText, MergeTagContext.Html, "{{ field.secret }} {{ entry.answers }}", false, "{{ field.secret }} {{ entry.answers }}");
        yield return Case(MergeTagPlace.DefaultValue, MergeTagContext.Text, "{{ field.secret }} {{ entry.answers }}", false, "{{ field.secret }} {{ entry.answers }}");
        yield return Case(MergeTagPlace.HiddenValue, MergeTagContext.Text, null, false, $"{bullets}|{bullets}|Name: Anna");
        yield return Case(MergeTagPlace.HiddenValue, MergeTagContext.Text, null, true, $"{bullets}|{bullets}|Name: Anna");
        yield return Case(MergeTagPlace.Outcome, MergeTagContext.Html, null, false, $"{bullets}|{bullets}|Name: Anna\nSecret: {bullets}\nChildren: {bullets}");
        yield return Case(MergeTagPlace.Outcome, MergeTagContext.Html, null, true, $"{bullets}|{bullets}|Name: Anna\nSecret: {bullets}\nChildren: {bullets}");
        yield return Case(MergeTagPlace.Redirect, MergeTagContext.Url, "https://example.org/?s={{ field.secret }}&a={{ entry.answers }}", false, $"https://example.org/?s={encoded}&a=Name%3A%20Anna");
        yield return Case(MergeTagPlace.Redirect, MergeTagContext.Url, "https://example.org/?s={{ field.secret }}", true, $"https://example.org/?s={encoded}");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, null, false, "||Name: Anna");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, null, true, "||Name: Anna");
        yield return Case(MergeTagPlace.Mail, MergeTagContext.Text, null, false, $"{bullets}|{bullets}|Name: Anna");
        yield return Case(MergeTagPlace.Mail, MergeTagContext.Text, null, true, "4111|Ben|Name: Anna\nSecret: 4111\nChildren:\n1. Child: Ben");
        yield return Case(MergeTagPlace.Action, MergeTagContext.Text, null, false, $"{bullets}|{bullets}|Name: Anna");
        yield return Case(MergeTagPlace.Action, MergeTagContext.Text, null, true, "4111|Ben|Name: Anna\nSecret: 4111\nChildren:\n1. Child: Ben");
    }

    [TestCaseSource(nameof(Places))]
    public async Task ASensitiveField_IsMaskedOrLeftOut_AsThePlaceDecides(MergeTagPlace place, MergeTagContext context, string? template, bool included, string expected)
    {
        var name = Field("text", "name", "Name");
        var secret = Field("text", "secret", "Secret") with { Sensitive = true };
        var child = Field("text", "child", "Child");
        var children = Field("repeater", "children", "Children") with { Fields = [child], Sensitive = true };
        var form = Form(name, secret, children);
        var answers = Answers(Answer(name, "Anna"), Answer(secret, "4111"), InRow(child, 0, "Ben"));
        var scope = Scope(form, answers, included: included ? new HashSet<Guid> { secret.Key, children.Key } : null);

        var rendered = await MergeTagTestSite.Create().RenderAsync(template ?? "{{ field.secret }}|{{ field.child }}|{{ entry.answers | default: \"-\" }}", scope, place, context);

        Assert.That(rendered.Text, Is.EqualTo(expected));
    }

    /// <summary>A character as JSON escapes it where markup could start: a backslash, <c>u</c> and four hex digits.</summary>
    private static string Escaped(char c) => "\\" + "u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);

    [Test]
    public async Task ASensitiveFieldWithoutAnAnswer_ShowsNoBullets()
    {
        var secret = Field("text", "secret", "Secret") with { Sensitive = true };

        var rendered = await MergeTagTestSite.Create().RenderAsync("[{{ field.secret }}]", Scope(Form(secret), Answers()), MergeTagPlace.Outcome, MergeTagContext.Html);

        Assert.That(rendered.Text, Is.EqualTo("[]"));
    }

    [Test]
    public async Task TheAnswers_InTheOutcome_MaskASensitiveField_InTheTable()
    {
        var secret = Field("text", "secret", "Secret") with { Sensitive = true };

        var rendered = await MergeTagTestSite.Create().RenderAsync("{{ entry.answers }}", Scope(Form(secret), Answers(Answer(secret, "4111"))), MergeTagPlace.Outcome, MergeTagContext.Html);

        Assert.That(rendered.Text, Is.EqualTo($"<table class=\"jfb-answers\"><tbody>\n<tr><th scope=\"row\">Secret</th><td>{Bullets}</td></tr>\n</tbody></table>"));
    }
}
