using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// What a visitor's browser renders a template with (ADR-0085): whether the template renders other than as it is written,
/// each path's value but answer recall's in a visitor text and the query's in a default value, each resolved once as a
/// rendering resolves it, the fields its answer-recall tags name, and the paths its place refuses (ADR-0118).
/// </summary>
public sealed class TemplateValuesTests
{
    [Test]
    public async Task AVisitorText_GetsItsPathsValues_ButAnswerRecalls_WhoseFieldsItNames()
    {
        var email = Field("email", "email", "Email");
        var topic = Field("text", "topic", "Topic");
        var remarks = Field("textarea", "remarks", "Remarks");
        var form = Form(email, topic, remarks);

        var found = await Values("{{ field.topic }} for {{ form.name | upper }}: {{ field.email.label }}, {{ field.topic.value }}", MergeTagPlace.VisitorText, form, remarks.Key);

        Assert.Multiple(() =>
        {
            Assert.That(found.Renders, Is.True);
            Assert.That(found.Values.Keys, Is.EqualTo(new[] { "form.name" }), "the browser fills in answer recall itself");
            Assert.That(found.Values["form.name"].Items.Single().Text, Is.EqualTo("Contact <us>"), "as the source gives it; the context encodes it when it renders");
            Assert.That(found.Recalled, Is.EqualTo(new[] { topic.Key, email.Key }), "each field once, in the order the text first names it");
        });
    }

    [Test]
    public async Task ATagThePlaceRefuses_IsWrittenAsItStands_SoItGetsNoValue_ButTheFieldsItNamesAreGiven()
    {
        var email = Field("email", "email", "Email");
        var topic = Field("text", "topic", "Topic");
        var form = Form(email, topic);

        var found = await Values("Hello {{ member.name }}, {{ field.topic }} and {{ field.email }}", MergeTagPlace.VisitorText, form, email.Key);

        Assert.Multiple(() =>
        {
            Assert.That(found.Renders, Is.False, "the member source is not for visitor texts, the topic comes after the text, and the email is the field whose text it is");
            Assert.That(found.Values, Is.Empty);
            Assert.That(found.Recalled, Is.EqualTo(new[] { topic.Key, email.Key }), "the browser refuses them by their places in the form, as the server does");
            Assert.That(found.Refused, Is.EqualTo(new[] { "member.name" }), "a source the place does not take, which the browser cannot tell from one no site has; the fields it refuses itself");
        });
    }

    [Test]
    public async Task ADefaultValue_LeavesItsQueryValuesToThePage_AndNamesEachSourceItsPlaceRefusesOnce()
    {
        var email = Field("email", "email", "Email");
        var notes = Field("textarea", "notes", "Notes");
        var form = Form(email, notes);

        var found = await Values("{{ query.ref }} at {{ form.name }} by {{ member.name }}, {{ field.email }} and {{ member.name | upper }}", MergeTagPlace.DefaultValue, form, notes.Key);

        Assert.Multiple(() =>
        {
            Assert.That(found.Renders, Is.True);
            Assert.That(found.Values.Keys, Is.EqualTo(new[] { "form.name" }), "the page fills in its own address's query values, which a cached page must not keep");
            Assert.That(found.Refused, Is.EqualTo(new[] { "member.name", "field.email" }), "each once, in the order the template first names it: a default recalls no answer");
            Assert.That(found.Recalled, Is.Empty);
        });
    }

    [Test]
    public async Task OnlyTheTemplatesOwnTextAndRefusedTags_RenderAsWritten()
    {
        var form = Form(Field("email", "email", "Email"));

        var plain = await Values("Plain text, with { braces }.", MergeTagPlace.VisitorText, form);
        var refused = await Values("{{ member.name }} and {{ entry.number }}", MergeTagPlace.VisitorText, form);
        var literal = await Values("{{ \"Hi\" | upper }}", MergeTagPlace.VisitorText, form);
        var unknown = await Values("[{{ form.nothing }}{{ nothing.at }}]", MergeTagPlace.VisitorText, form);
        var stopped = await Values(string.Concat(Enumerable.Repeat("{{ member.name }}", 201)), MergeTagPlace.VisitorText, form);

        Assert.Multiple(() =>
        {
            Assert.That((plain.Renders, refused.Renders), Is.EqualTo((false, false)));
            Assert.That((literal.Renders, literal.Values.Count), Is.EqualTo((true, 0)), "a string renders through its filters, with no value");
            Assert.That((unknown.Renders, unknown.Values.Count), Is.EqualTo((true, 0)), "a path no source knows, and a source the site lacks, render as nothing, with no value");
            Assert.That((stopped.Renders, stopped.Values.Count), Is.EqualTo((true, 0)), "more tags than a text renders end its output, which the browser repeats");
        });
    }

    [Test]
    public async Task APathNamedTwice_IsResolvedOnce()
    {
        var calls = 0;
        var site = MergeTagTestSite.Create(sources: [new CountingSource(() => Interlocked.Increment(ref calls))]);

        var found = await site.Service.ValuesAsync("{{ count.a }}, {{ count.a | upper }} and {{ count.b }}", MergeTagPlace.VisitorText, MergeTagContext.Html, MergeTagTestSite.Scope());

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(found.Values.Keys, Is.EquivalentTo(new[] { "count.a", "count.b" }));
        });
    }

    [Test]
    public async Task AnotherPlace_GetsTheFieldsValuesToo_AndRecallsNothing()
    {
        var email = Field("email", "email", "Email");
        var form = Form(email);
        var scope = Scope(form, Answers(Answer(email, "anna@example.org")));

        var found = await MergeTagTestSite.Create().Service.ValuesAsync("{{ field.email }}", MergeTagPlace.Mail, MergeTagContext.Text, scope);

        Assert.Multiple(() =>
        {
            Assert.That(found.Values["field.email"].Items.Single().Text, Is.EqualTo("anna@example.org"));
            Assert.That(found.Recalled, Is.Empty);
        });
    }

    [Test]
    public void AContextThePlaceDoesNotRenderIn_IsRefused()
    {
        var service = MergeTagTestSite.Create().Service;

        Assert.ThrowsAsync<ArgumentException>(() => service.ValuesAsync("{{ form.name }}", MergeTagPlace.VisitorText, MergeTagContext.Url, MergeTagTestSite.Scope()));
    }

    private static Task<MergeTagTemplateValues> Values(string text, MergeTagPlace place, Core.Definitions.FormDefinition form, Guid? owner = null) =>
        MergeTagTestSite.Create().Service.ValuesAsync(text, place, MergeTagContext.Text, MergeTagTestSite.Scope(form: form) with { TextOwner = owner });

    /// <summary>A site's source that counts its calls and gives <c>x</c> for every path.</summary>
    private sealed class CountingSource(Action called) : IFormBuilderMergeTagSource
    {
        public string Name => "count";

        public bool IsPerVisitor => false;

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
        {
            called();
            return ValueTask.FromResult<MergeTagValue?>(MergeTagValue.Of("x"));
        }
    }
}
