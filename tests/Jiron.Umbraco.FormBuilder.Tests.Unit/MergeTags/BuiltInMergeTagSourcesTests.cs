using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// The built-in sources of this version (ADR-0065): <c>form</c>, <c>entry</c>, <c>query</c>, <c>context</c> and
/// <c>date</c>, each value escaped by its context, the dates in the site's time zone and the culture's formats.
/// </summary>
public sealed class BuiltInMergeTagSourcesTests
{
    // 2026-03-07 09:15 UTC, 10:15 in Zurich.
    private static readonly DateTimeOffset March = new(2026, 3, 7, 9, 15, 0, TimeSpan.Zero);

    [Test]
    public async Task TheForm_GivesItsNameAndAlias_EscapedByTheContext()
    {
        var site = Create();

        var html = await site.RenderAsync("{{ form.name }} ({{ form.alias }})", context: MergeTagContext.Html);
        var text = await site.RenderAsync("{{ form.name }}");

        Assert.Multiple(() =>
        {
            Assert.That(html.Text, Is.EqualTo("Contact &lt;us&gt; (contact)"));
            Assert.That(text.Text, Is.EqualTo("Contact <us>"));
            Assert.That(html.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task TheEntry_BeforeTheVisitorSubmits_IsEmpty_WithoutAProblem()
    {
        var rendered = await Create().RenderAsync("[{{ entry.number }}|{{ entry.reference }}|{{ entry.date }}|{{ entry.state }}]");

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[|||]"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task TheEntry_GivesItsNumberReferenceDateAndState_InTheSitesTimeZone()
    {
        var site = Create(zone: "Europe/Zurich");
        var scope = Scope(entry: Entry(March, state: "approved"));

        var rendered = await site.RenderAsync("{{ entry.number }} {{ entry.reference }} {{ entry.date }} {{ entry.state }}", scope);
        var wire = await site.RenderAsync("{{ entry.date | date: \"iso\" }} {{ entry.number | number: 2 }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("42 2026-00042 07/03/2026 10:15 approved"));
            Assert.That(wire.Text, Is.EqualTo("2026-03-07T10:15:00 42.00"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task TheEntrysDate_IsWrittenInTheCulture()
    {
        var rendered = await Create().RenderAsync("{{ entry.date }}", Scope(entry: Entry(March), culture: "de-CH"));

        Assert.That(rendered.Text, Is.EqualTo("07.03.2026 09:15"));
    }

    [TestCase("UTC", "2025-00042")]
    [TestCase("Europe/Zurich", "2026-00042")]
    [TestCase("Not/A_Zone", "2025-00042")]
    public async Task TheReferencesYear_IsTheYearInTheSitesTimeZone_OrUtcWhenTheZoneIsUnknown(string zone, string reference)
    {
        // 23:30 UTC on New Year's Eve is already 2026 in Zurich.
        var rendered = await Create(zone: zone).RenderAsync("{{ entry.reference }}", Scope(entry: Entry(new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero))));

        Assert.That(rendered.Text, Is.EqualTo(reference));
    }

    [Test]
    public async Task APathTheEntryDoesNotHave_RendersEmpty_WithAWarning()
    {
        var rendered = await Create().RenderAsync("a{{ entry.colour }}b", Scope(entry: Entry(March)));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("ab"));
            Assert.That(rendered.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 4) }));
        });
    }

    [Test]
    public async Task AQueryValue_IsGivenOnlyForANameTheFormAllows_ExactlyAsSpelt()
    {
        var site = Create();
        var scope = Scope(
            form: Form(query: ["utm_source", "tag", "campaign"]),
            query: new Dictionary<string, IReadOnlyList<string>>
            {
                ["utm_source"] = ["mail"],
                ["tag"] = ["a", "b"],
                ["secret"] = ["s3cret"],
            });

        var allowed = await site.RenderAsync("{{ query.utm_source }}|{{ query.tag }}|{{ query.campaign }}", scope);
        var refused = await site.RenderAsync("[{{ query.secret }}][{{ query.UTM_SOURCE }}]", scope);

        Assert.Multiple(() =>
        {
            Assert.That(allowed.Text, Is.EqualTo("mail|a, b|"));
            Assert.That(allowed.Problems, Is.Empty);
            Assert.That(refused.Text, Is.EqualTo("[][]"));
            Assert.That(refused.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.UnknownPath, MergeTagProblems.UnknownPath }));
        });
    }

    [Test]
    public async Task AContextValue_IsWhatTheDeveloperPassed_AndAListedOneIsKnownWithoutAValue()
    {
        var site = Create();
        var scope = Scope(form: Form(context: ["segment", "campaign"]), context: new Dictionary<string, string> { ["campaign"] = "spring", ["extra"] = "passed" });

        var rendered = await site.RenderAsync("{{ context.campaign }}|{{ context.segment }}|{{ context.extra }}|{{ context.other }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("spring||passed|"));
            Assert.That(rendered.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.UnknownPath }));
        });
    }

    [Test]
    public async Task Now_IsTheClocksTime_InTheSitesTimeZone()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var site = Create(zone: "America/New_York", time: time);

        var rendered = await site.RenderAsync("{{ date.now }} {{ date.now | date: \"iso\" }} {{ date.now | date: \"long\" }}");
        time.Advance(TimeSpan.FromMinutes(90));
        var later = await site.RenderAsync("{{ date.now | date: \"time\" }}");

        // In the scope's culture (British English) as its culture data writes it, which differs between systems (ADR-0263).
        var british = CultureInfo.GetCultureInfo("en-GB");
        var evening = new DateTime(2025, 12, 31, 19, 0, 0, DateTimeKind.Unspecified);
        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo($"{evening.ToString("g", british)} 2025-12-31T19:00:00 {evening.ToString("D", british)}"));
            Assert.That(later.Text, Is.EqualTo(evening.AddMinutes(90).ToString("t", british)));
        });
    }

    // The British long date as Windows' ICU writes it, with a comma after the day's name, and as Linux's does, without:
    // the product prints each as its culture data says, with formats made here (ROADMAP 26.1, ADR-0263).
    [TestCase("dddd, d MMMM yyyy", "Wednesday, 31 December 2025")]
    [TestCase("dddd d MMMM yyyy", "Wednesday 31 December 2025")]
    public async Task Now_InTheLongFormat_IsWrittenAsTheCulturesDataSays(string longDate, string expected)
    {
        var british = DotNetCultureFormats.From(CultureInfo.InvariantCulture) with
        {
            Dates = new DatePatterns(@"dd\/MM\/yyyy", "d MMM yyyy", longDate, @"HH\:mm", @"dd\/MM\/yyyy HH\:mm"),
        };
        var time = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var site = Create(zone: "America/New_York", time: time, cultures: new MergeTagCultures([KeyValuePair.Create("en-GB", british)]));

        var rendered = await site.RenderAsync("{{ date.now | date: \"long\" }}|{{ date.now }}");

        Assert.That(rendered.Text, Is.EqualTo(expected + "|31/12/2025 19:00"));
    }

    [Test]
    public async Task EachPath_IsResolvedOnce_ForATemplateThatNamesItTwice()
    {
        var calls = 0;
        var source = new CountingSource(() => calls++);
        var site = Create([source]);

        var rendered = await site.RenderAsync("{{ count.it }} {{ count.it | upper }} {{ count.other }}");

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("x X x"));
            Assert.That(calls, Is.EqualTo(2));
        });
    }

    [TestCase(null, 42, 2026, "2026-00042")]
    [TestCase("REQ-", 42, 2026, "REQ-2026-00042")]
    [TestCase("", 7, 2026, "2026-00007")]
    [TestCase("REQ-", 123456, 2026, "REQ-2026-123456")]
    public void TheReference_HasThePrefix_TheYear_AndTheNumberPadded(string? prefix, int number, int year, string reference)
    {
        Assert.That(EntryReferences.Format(new EntryReferenceFormat { Prefix = prefix }, number, year), Is.EqualTo(reference));
    }

    [TestCase(false, 5, "T-00042")]
    [TestCase(false, 1, "T-42")]
    [TestCase(true, 3, "T-2026-042")]
    [TestCase(true, 10, "T-2026-0000000042")]
    [TestCase(true, 0, "T-2026-42")]
    [TestCase(true, 11, "T-2026-0000000042")]
    public void TheReference_LeavesTheYearOut_OrPadsToItsDigits_FromOneToTen(bool includeYear, int digits, string reference)
    {
        Assert.That(EntryReferences.Format(new EntryReferenceFormat { Prefix = "T-", IncludeYear = includeYear, Digits = digits }, 42, 2026), Is.EqualTo(reference));
    }

    [Test]
    public void WithoutAFormat_TheReferenceIsTheYearAndFiveDigits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EntryReferences.Format(null, 42, 2026), Is.EqualTo("2026-00042"));
            Assert.That(EntryReferences.Format(null, int.MaxValue, 987), Is.EqualTo("0987-2147483647"));
        });
    }

    [Test]
    public async Task TheFormsFormat_IsTheOneTheEntryReferenceIsWrittenIn()
    {
        var form = Form(reference: new EntryReferenceFormat { Prefix = "INV/", IncludeYear = false, Digits = 6 });

        var rendered = await Create().RenderAsync("{{ entry.reference }}", Scope(form: form, entry: Entry(March)));

        Assert.That(rendered.Text, Is.EqualTo("INV/000042"));
    }

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
