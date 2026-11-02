using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// What each place takes (ADR-0014, ADR-0065): its contexts, the built-in sources it may name, a site's sources by their
/// traits, and a site's filters only where the server alone renders. A refused source or filter renders as written.
/// </summary>
public sealed class MergeTagPlacesTests
{
    private static readonly IFormBuilderMergeTagSource Shared = new SiteSource("brand", perVisitor: false);
    private static readonly IFormBuilderMergeTagSource PerVisitor = new SiteSource("crm", perVisitor: true);
    private static readonly IFormBuilderMergeTagSource MailOnly = new SiteSource("ledger", perVisitor: false, places: [MergeTagPlace.Mail]);

    public static IEnumerable<TestCaseData> RefusedSources()
    {
        static TestCaseData Case(MergeTagPlace place, MergeTagContext context, string template) =>
            new TestCaseData(place, context, template).SetName($"{place} refuses {template}");

        // The built-in sources a place's list lacks.
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Html, "{{ entry.number }}");
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Text, "{{ query.tag }}");
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Html, "{{ context.campaign }}");
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Html, "{{ member.name }}");
        yield return Case(MergeTagPlace.ConsentText, MergeTagContext.Html, "{{ date.now }}");
        yield return Case(MergeTagPlace.ConsentText, MergeTagContext.Html, "{{ field.email }}");
        yield return Case(MergeTagPlace.ConsentText, MergeTagContext.Html, "{{ query.tag }}");
        yield return Case(MergeTagPlace.DefaultValue, MergeTagContext.Text, "{{ entry.reference }}");
        yield return Case(MergeTagPlace.DefaultValue, MergeTagContext.Text, "{{ context.campaign }}");
        yield return Case(MergeTagPlace.DefaultValue, MergeTagContext.Text, "{{ field.email }}");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, "{{ date.now }}");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, "{{ query.tag }}");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, "{{ site.name }}");

        // A site's source whose values are per visitor, in a place a cached page may show.
        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Html, "{{ crm.segment }}");
        yield return Case(MergeTagPlace.DefaultValue, MergeTagContext.Text, "{{ crm.segment }}");

        // Any site's source where only built-in ones may be named, and one outside the places it chose.
        yield return Case(MergeTagPlace.ConsentText, MergeTagContext.Html, "{{ brand.name }}");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, "{{ brand.name }}");
        yield return Case(MergeTagPlace.Outcome, MergeTagContext.Html, "{{ ledger.total }}");
    }

    public static IEnumerable<TestCaseData> TakenSources()
    {
        static TestCaseData Case(MergeTagPlace place, MergeTagContext context, string template, string text) =>
            new TestCaseData(place, context, template, text).SetName($"{place} takes {template}");

        yield return Case(MergeTagPlace.VisitorText, MergeTagContext.Html, "{{ form.name }} {{ brand.name }}", "Contact &lt;us&gt; Brand");
        yield return Case(MergeTagPlace.ConsentText, MergeTagContext.Html, "{{ form.alias }}", "contact");
        yield return Case(MergeTagPlace.DefaultValue, MergeTagContext.Text, "{{ query.tag }}{{ brand.name }}", "aBrand");
        yield return Case(MergeTagPlace.HiddenValue, MergeTagContext.Text, "{{ crm.segment }} {{ context.campaign }}", "Crm spring");
        yield return Case(MergeTagPlace.EntryTitle, MergeTagContext.Text, "{{ form.alias }} {{ entry.number }}", "contact ");
        yield return Case(MergeTagPlace.Mail, MergeTagContext.Header, "{{ ledger.total }} {{ crm.segment }}", "Ledger Crm");
        yield return Case(MergeTagPlace.Redirect, MergeTagContext.Url, "https://example.org/?s={{ crm.segment }}", "https://example.org/?s=Crm");
        yield return Case(MergeTagPlace.Action, MergeTagContext.Json, "{{ crm.segment }}", "\"Crm\"");
    }

    [TestCaseSource(nameof(RefusedSources))]
    public async Task APlace_RefusesASource_ItDoesNotTake_RenderingTheTagAsWritten(MergeTagPlace place, MergeTagContext context, string template)
    {
        var site = Create([Shared, PerVisitor, MailOnly]);

        var rendered = await site.Service.RenderAsync("a" + template, place, context, ScopeWithValues());
        var checkedProblems = site.Service.Check("a" + template, place, context, ScopeWithValues().Form);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("a" + template));
            Assert.That(rendered.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.SourceNotForPlace, 4) }));
            Assert.That(checkedProblems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.SourceNotForPlace, 4) }));
        });
    }

    [TestCaseSource(nameof(TakenSources))]
    public async Task APlace_TakesTheSourcesItLists(MergeTagPlace place, MergeTagContext context, string template, string text)
    {
        var site = Create([Shared, PerVisitor, MailOnly]);

        var rendered = await site.Service.RenderAsync(template, place, context, ScopeWithValues());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(text));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task APathItsSourceDoesNotKnow_IsNotAProblemOfThePlace_ButAnUnknownPath()
    {
        // VisitorText takes page, whose paths are one property alias, or up and one.
        var rendered = await Create().Service.RenderAsync("[{{ page.title.text }}]", MergeTagPlace.VisitorText, MergeTagContext.Html, Scope());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[]"));
            Assert.That(rendered.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 4) }));
        });
    }

    [TestCase(MergeTagPlace.VisitorText, MergeTagContext.Html)]
    [TestCase(MergeTagPlace.ConsentText, MergeTagContext.Html)]
    [TestCase(MergeTagPlace.DefaultValue, MergeTagContext.Text)]
    public async Task APlaceTheBrowserRendersToo_RefusesASitesFilter(MergeTagPlace place, MergeTagContext context)
    {
        var site = Create(filters: [new ShoutFilter()]);

        var rendered = await site.Service.RenderAsync("{{ form.alias | shout }}", place, context, Scope());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("{{ form.alias | shout }}"));
            Assert.That(rendered.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.FilterNotForPlace }));
            Assert.That(rendered.Problems.Single().At, Is.EqualTo("{{ form.alias | ".Length));
        });
    }

    [TestCase(MergeTagPlace.HiddenValue, MergeTagContext.Text)]
    [TestCase(MergeTagPlace.Outcome, MergeTagContext.Html)]
    [TestCase(MergeTagPlace.EntryTitle, MergeTagContext.Text)]
    [TestCase(MergeTagPlace.Mail, MergeTagContext.Text)]
    [TestCase(MergeTagPlace.Action, MergeTagContext.Text)]
    public async Task APlaceOnlyTheServerRenders_TakesASitesFilter(MergeTagPlace place, MergeTagContext context)
    {
        var rendered = await Create(filters: [new ShoutFilter()]).Service.RenderAsync("{{ form.alias | shout }}", place, context, Scope());

        Assert.That(rendered.Text, Is.EqualTo("CONTACT!"));
    }

    [TestCase(MergeTagPlace.VisitorText, MergeTagContext.Url)]
    [TestCase(MergeTagPlace.ConsentText, MergeTagContext.Text)]
    [TestCase(MergeTagPlace.DefaultValue, MergeTagContext.Html)]
    [TestCase(MergeTagPlace.HiddenValue, MergeTagContext.Json)]
    [TestCase(MergeTagPlace.Outcome, MergeTagContext.Text)]
    [TestCase(MergeTagPlace.Redirect, MergeTagContext.Html)]
    [TestCase(MergeTagPlace.EntryTitle, MergeTagContext.Header)]
    [TestCase(MergeTagPlace.Mail, MergeTagContext.Url)]
    public void AContextThePlaceLacks_IsRefused_BeforeAnythingRenders(MergeTagPlace place, MergeTagContext context)
    {
        var service = Create().Service;

        Assert.Multiple(() =>
        {
            Assert.That(() => service.RenderAsync("{{ form.name }}", place, context, Scope()), Throws.ArgumentException.With.Property("ParamName").EqualTo("context"));
            Assert.That(() => service.Check("{{ form.name }}", place, context, Form()), Throws.ArgumentException.With.Property("ParamName").EqualTo("context"));
        });
    }

    [Test]
    public async Task AnActionsText_RendersInEveryContext()
    {
        var service = Create().Service;
        var texts = new List<string>();
        foreach (var context in Enum.GetValues<MergeTagContext>())
        {
            // An address takes tags only after its host, so every context gets one.
            texts.Add((await service.RenderAsync("https://example.org/?f={{ form.alias }}", MergeTagPlace.Action, context, Scope())).Text);
        }

        Assert.That(texts, Has.Count.EqualTo(Enum.GetValues<MergeTagContext>().Length).And.All.Contains("contact"));
    }

    [Test]
    public void APlaceThatIsNotOne_IsRefused()
    {
        Assert.That(() => Create().Service.Paths((MergeTagPlace)99, Form()), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ThePicker_OffersThePathsOfTheSourcesEachPlaceTakes_TheBuiltInOnesFirst()
    {
        var service = Create([Shared, PerVisitor, MailOnly]).Service;
        var form = Form(query: ["tag"], context: ["campaign"]);

        string[] Paths(MergeTagPlace place) => [.. service.Paths(place, form).Select(path => path.Path)];

        Assert.Multiple(() =>
        {
            Assert.That(Paths(MergeTagPlace.VisitorText), Is.EqualTo(new[] { "form.name", "form.alias", "page.name", "page.url", "site.name", "site.url", "date.now", "brand.name" }));
            Assert.That(Paths(MergeTagPlace.ConsentText), Is.EqualTo(new[] { "form.name", "form.alias", "page.name", "page.url", "site.name", "site.url" }));
            Assert.That(Paths(MergeTagPlace.DefaultValue), Is.EqualTo(new[] { "form.name", "form.alias", "page.name", "page.url", "site.name", "site.url", "query.tag", "date.now", "brand.name" }));
            Assert.That(Paths(MergeTagPlace.EntryTitle), Is.EqualTo(new[] { "entry.number", "entry.reference", "entry.date", "entry.state", "entry.url", "entry.answers", "entry.table", "form.name", "form.alias" }));
            Assert.That(Paths(MergeTagPlace.Outcome), Is.EqualTo(new[]
            {
                "entry.number", "entry.reference", "entry.date", "entry.state", "entry.url", "entry.answers", "entry.table", "form.name", "form.alias", "page.name", "page.url", "site.name", "site.url",
                "member.name", "member.email", "query.tag", "context.campaign", "date.now", "brand.name", "crm.segment",
            }));
            Assert.That(Paths(MergeTagPlace.Mail), Does.Contain("ledger.total"));
        });
    }

    // A scope with a value for each source the cases name.
    private static MergeTagScope ScopeWithValues() => Scope(
        form: Form(query: ["tag"], context: ["campaign"]),
        query: new Dictionary<string, IReadOnlyList<string>> { ["tag"] = ["a"] },
        context: new Dictionary<string, string> { ["campaign"] = "spring" });

    /// <summary>A site's source that gives its own name, capitalised, for every path, and offers one path.</summary>
    private sealed class SiteSource(string name, bool perVisitor, MergeTagPlace[]? places = null) : IFormBuilderMergeTagSource
    {
        private readonly string _path = perVisitor ? "segment" : places is null ? "name" : "total";

        public string Name => name;

        public bool IsPerVisitor => perVisitor;

        public IReadOnlySet<MergeTagPlace>? Places => places?.ToHashSet();

        public IEnumerable<MergeTagPathInfo> Paths(Core.Definitions.FormDefinition form) => [new($"{name}.{_path}")];

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<MergeTagValue?>(MergeTagValue.Of(char.ToUpperInvariant(name[0]) + name[1..]));
    }

    /// <summary>A site's filter that writes each item in capitals with an exclamation mark.</summary>
    private sealed class ShoutFilter : IFormBuilderMergeTagFilter
    {
        public string Name => "shout";

        public bool Accepts(IReadOnlyList<MergeTagArgument> arguments) => arguments.Count == 0;

        public MergeTagValue Apply(MergeTagFilterRequest request) =>
            new([.. request.Value.Items.Select(item => new MergeTagItem(item.Text.ToUpperInvariant() + "!"))], request.Value.IsList);
    }
}
