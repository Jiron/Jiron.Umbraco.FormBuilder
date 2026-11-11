using System.Collections.Immutable;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Samples.Calendar;
using Jiron.Umbraco.FormBuilder.Samples.Captcha;
using Jiron.Umbraco.FormBuilder.Samples.Notifications;
using Jiron.Umbraco.FormBuilder.Samples.Products;
using Jiron.Umbraco.FormBuilder.Samples.Scanning;
using Jiron.Umbraco.FormBuilder.Samples.Spam;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Samples;

/// <summary>
/// The samples that run on the server alone (docs/extending.md, ADR-0234): the product source, the iCalendar export, the
/// arithmetic captcha, the link density check, the "eicar" scanner and the blocked domains handler, each as a site gets
/// it.
/// </summary>
public sealed class ServerSampleTests
{
    // ---- Products ----------------------------------------------------------------------------------------------------

    [Test]
    public async Task TheProducts_InStock_ComeWithTheirPrice_UnderTheirCategory_InTheVisitorsLanguage()
    {
        var source = new ProductOptionSource(new FakeProductCatalogue());
        var english = await source.GetOptionsAsync(new FormBuilderOptionRequest("en-US", [], null), CancellationToken.None);
        var german = await source.GetOptionsAsync(new FormBuilderOptionRequest("de-CH", ["gifts"], null), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(source.Name, Is.EqualTo("sampleProducts"));
            Assert.That(english.Select(option => option.Value), Is.EqualTo(new[] { "B-101", "B-102", "C-201", "C-202", "D-301" }), "the bookmarks are out of stock");
            Assert.That((english[0].Label, english[0].Description, english[0].ParentValues), Is.EqualTo(("Library tote bag", "12.50 EUR", (IReadOnlyList<string>)["gifts"])));
            Assert.That((german[1].Label, german[1].Description), Is.EqualTo(("Leselampe", "34.90 EUR")), "Swiss German writes its decimals with a point");
            Assert.That(german, Has.Count.EqualTo(5), "the option service filters by the parent's answers, not the source");
        });
    }

    // ---- iCalendar ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task ABooking_IsAnEventOnItsDay_FromItsTime_ForItsHours_WithItsAnswers()
    {
        var key = Guid.Parse("4f8e2b1c-0d3a-4e5f-9a6b-7c8d9e0f1a2b");
        var export = Export(
            [
                Column(FormBuilderExportColumnKind.Number, "Entry", "number"),
                Column(FormBuilderExportColumnKind.Language, "Language", "language"),
                Column(FormBuilderExportColumnKind.Answer, "Day", "day"),
                Column(FormBuilderExportColumnKind.Answer, "From", "from"),
                Column(FormBuilderExportColumnKind.Answer, "Hours", "hours"),
                Column(FormBuilderExportColumnKind.Answer, "Room", "room"),
                Column(FormBuilderExportColumnKind.Answer, "Email", "email"),
            ],
            new FormBuilderExportEntry(key, 7, new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero), [["7"], ["de-DE"], ["20.11.2026"], ["14:30"], ["2,5"], ["Reading room; first floor"], ["ada@visitor.jfb.test"]], [], []));

        var written = await WriteAsync(export);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.StartWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n"));
            Assert.That(written, Does.EndWith("END:VEVENT\r\nEND:VCALENDAR\r\n"));
            Assert.That(written, Does.Contain("\r\nUID:4f8e2b1c0d3a4e5f9a6b7c8d9e0f1a2b@jiron-form-builder\r\n"));
            Assert.That(written, Does.Contain("\r\nDTSTAMP:20261102T093000Z\r\n"));
            Assert.That(written, Does.Contain("\r\nDTSTART:20261120T143000\r\nDTEND:20261120T170000\r\n"), "German dates and two and a half hours");
            Assert.That(written, Does.Contain("\r\nSUMMARY:Room hire #7\r\n"));
            Assert.That(written.Replace("\r\n ", string.Empty, StringComparison.Ordinal), Does.Contain("Room: Reading room\\; first floor"), "escaped, once unfolded");
            Assert.That(written, Does.Contain("\r\nATTENDEE;ROLE=REQ-PARTICIPANT:mailto:ada@visitor.jfb.test\r\n"));
            Assert.That(written.Split("\r\n").Select(line => Encoding.UTF8.GetByteCount(line)), Is.All.LessThanOrEqualTo(75), "folded");
        });
    }

    [Test]
    public async Task AnEntryWithoutATime_IsAnEventOfItsDay_AndOneWithoutADate_OfTheDayItCame()
    {
        var export = Export(
            [Column(FormBuilderExportColumnKind.Language, "Language", "language"), Column(FormBuilderExportColumnKind.Answer, "Day", "day")],
            new FormBuilderExportEntry(Guid.NewGuid(), 1, new DateTimeOffset(2026, 11, 2, 23, 30, 0, TimeSpan.Zero), [["en-US"], ["11/20/2026"]], [], []),
            new FormBuilderExportEntry(Guid.NewGuid(), 2, new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero), [["en-US"], []], [], []));

        var written = await WriteAsync(export);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("\r\nDTSTART;VALUE=DATE:20261120\r\nDTEND;VALUE=DATE:20261121\r\n"));
            Assert.That(written, Does.Contain("\r\nDTSTART;VALUE=DATE:20261103\r\nDTEND;VALUE=DATE:20261104\r\n"));
            Assert.That(written.Split("BEGIN:VEVENT"), Has.Length.EqualTo(3));
        });
    }

    [Test]
    public void ALongLine_IsFoldedAt75Octets_WithoutSplittingACharacter()
    {
        var line = "DESCRIPTION:" + string.Concat(Enumerable.Repeat("Gr" + (char)0xFC + (char)0xDF + "e ", 30));
        var folded = ICalendarExportFormat.Fold(line).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(folded.Select(part => Encoding.UTF8.GetByteCount(part)), Is.All.LessThanOrEqualTo(75));
            Assert.That(folded.Skip(1), Is.All.StartsWith(" "));
            Assert.That(folded[0] + string.Concat(folded.Skip(1).Select(part => part[1..])), Is.EqualTo(line));
            Assert.That(ICalendarExportFormat.Escape("a\\b;c,d\r\ne"), Is.EqualTo("a\\\\b\\;c\\,d\\ne"));
        });
    }

    // ---- The arithmetic captcha --------------------------------------------------------------------------------------

    [Test]
    public async Task ASum_PassesWithItsAnswer_AndFailsWithAnother_Late_OrTampered()
    {
        var clock = new TestClock();
        var provider = new ArithmeticCaptchaProvider(new EphemeralDataProtectionProvider(), new HttpContextAccessor(), clock);
        var client = provider.Challenge("https://site.test/jfb-samples/arithmetic-captcha.js");
        var (sum, sealedAnswer) = (client.SiteKey[..client.SiteKey.IndexOf(':')], client.SiteKey[(client.SiteKey.IndexOf(':') + 1)..]);
        var answer = sum.Split('+').Sum(int.Parse);

        async Task<FormBuilderCaptchaOutcome> Verify(string token) =>
            await provider.VerifyAsync(new FormBuilderCaptchaCheck(token, new HashSet<string>(), null), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(sum, Does.Match(@"^[1-9]\+[1-9]$"));
            Assert.That((client.ScriptGlobal, client.Action), Is.EqualTo(("jfbSampleArithmetic", (string?)null)));
            Assert.That(await Verify($"{sealedAnswer}:{answer}"), Is.EqualTo(FormBuilderCaptchaOutcome.Passed));
            Assert.That(await Verify($"{sealedAnswer}:{answer + 1}"), Is.EqualTo(FormBuilderCaptchaOutcome.Failed));
            Assert.That(await Verify($"{sealedAnswer}x:{answer}"), Is.EqualTo(FormBuilderCaptchaOutcome.Failed), "tampered");
            Assert.That(await Verify($"{answer}"), Is.EqualTo(FormBuilderCaptchaOutcome.Failed), "no sealed answer");
            Assert.That(await Verify($"{sealedAnswer}:-{answer}"), Is.EqualTo(FormBuilderCaptchaOutcome.Failed), "no number");
        });

        clock.Advance(ArithmeticCaptchaProvider.Lifetime + TimeSpan.FromSeconds(1));
        Assert.That(await Verify($"{sealedAnswer}:{answer}"), Is.EqualTo(FormBuilderCaptchaOutcome.Failed), "too late");
    }

    [Test]
    public void TheCaptcha_IsShownOnlyForARequest_WithItsScriptFromTheRequestsSite()
    {
        var requests = new HttpContextAccessor();
        var provider = new ArithmeticCaptchaProvider(new EphemeralDataProtectionProvider(), requests, new TestClock());
        var outside = provider.Client;
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("library.test");
        requests.HttpContext = context;

        Assert.Multiple(() =>
        {
            Assert.That(outside, Is.Null);
            Assert.That(provider.Client?.ScriptAddress, Is.EqualTo("https://library.test/jfb-samples/arithmetic-captcha.js"));
            Assert.That(provider.Client?.SiteKey, Is.Not.EqualTo(provider.Client?.SiteKey), "a new sum each time");
            Assert.That((provider.Name, provider.IsConfigured), Is.EqualTo(("sampleArithmetic", true)));
        });
    }

    // ---- Link density ------------------------------------------------------------------------------------------------

    [TestCase(new[] { "I mend books on Saturdays." }, null)]
    [TestCase(new[] { "See https://library.test/events for the times." }, null)]
    [TestCase(new[] { "Two links https://a.test and www.b.test in a sentence of quite a few words that goes on." }, null)]
    [TestCase(new[] { "https://a.test", "https://b.test", "https://c.test" }, 30)]
    [TestCase(new[] { "Cheap https://a.test https://b.test https://c.test https://d.test https://e.test https://f.test https://g.test https://h.test https://i.test https://j.test https://k.test" }, 100)]
    [TestCase(new[] { "Visit https://a.test now" }, 10)]
    public void Links_CountAgainstTheirWords(string[] texts, int? score)
    {
        var signal = LinkDensitySpamCheck.SignalOf(texts);

        Assert.That(signal is null ? null : (int?)signal.Score, Is.EqualTo(score));
        Assert.That(signal?.Reason, Is.EqualTo(score is null ? null : "link_density"));
    }

    // ---- The eicar scanner -------------------------------------------------------------------------------------------

    [TestCase("eicar.com", FormBuilderFileScanOutcome.Quarantine)]
    [TestCase("my-EICAR-test.txt", FormBuilderFileScanOutcome.Quarantine)]
    [TestCase("cv.pdf", FormBuilderFileScanOutcome.Clean)]
    public async Task AFileNamedEicar_IsQuarantined(string name, FormBuilderFileScanOutcome outcome)
    {
        await using var bytes = new MemoryStream([1, 2, 3]);

        Assert.That(await new EicarFileScanner().ScanAsync(new FormBuilderFileToScan(Guid.NewGuid(), name, "application/pdf", 3, "00", bytes), CancellationToken.None), Is.EqualTo(outcome));
    }

    // ---- Blocked domains ---------------------------------------------------------------------------------------------

    [Test]
    public async Task AnAddressAtABlockedDomain_IsRefusedAtItsField_InTheVisitorsLanguage()
    {
        var work = new FormField { Key = Guid.NewGuid(), Type = "email", Alias = "work" };
        var home = new FormField { Key = Guid.NewGuid(), Type = "email", Alias = "home" };
        var name = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name" };
        var options = new BlockedDomainsOptions();
        options.Domains.Add("Throwaway.test");
        var monitor = Substitute.For<IOptionsMonitor<BlockedDomainsOptions>>();
        monitor.CurrentValue.Returns(options);
        var notification = Submitting("de-CH", [work, home, name], (work, "ada@throwaway.TEST"), (home, "ada@home.test"), (name, "Ada@throwaway.test"));

        await new BlockedDomainsHandler(monitor).HandleAsync(notification, CancellationToken.None);

        Assert.That(notification.Errors.Select(error => (error.Field, error.Message)), Is.EqualTo(new[] { ((string?)"work", "Bitte geben Sie eine Adresse an, die Sie dauerhaft nutzen.") }));
        Assert.That(BlockedDomainsHandler.MessageIn("fr-FR"), Is.EqualTo("Please give an address you use for good."));
    }

    private static FormBuilderExportColumn Column(FormBuilderExportColumnKind kind, string label, string alias) =>
        new(kind, label, alias, kind == FormBuilderExportColumnKind.Answer ? alias : null);

    private static FormBuilderExport Export(IReadOnlyList<FormBuilderExportColumn> columns, params FormBuilderExportEntry[] entries) => new()
    {
        FormName = "Room hire",
        FormAlias = "roomHire",
        Culture = "en-US",
        Columns = columns,
        Tables = [],
        Options = new FormBuilderExportOptions(),
        Entries = entries.ToAsyncEnumerable(),
        OpenFileAsync = (_, _) => Task.FromResult<Stream?>(null),
    };

    private static async Task<string> WriteAsync(FormBuilderExport export)
    {
        await using var output = new MemoryStream();
        await new ICalendarExportFormat().WriteAsync(export, output, CancellationToken.None);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static EntrySubmittingNotification Submitting(string culture, FormField[] fields, params (FormField Field, string Text)[] answers)
    {
        var form = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages = [new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [.. fields] }] }],
        };
        return new EntrySubmittingNotification(new EventMessages())
        {
            FormKey = form.Key,
            FormAlias = "contact",
            Form = form,
            Version = 1,
            Culture = culture,
            Answers = new MergeTagAnswers(answers.Select(answer => new MergeTagAnswer(answer.Field.Key, ImmutableArray.Create(new FieldValue(answer.Text))))),
        };
    }
}
