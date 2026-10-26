using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Merge-tag templates an editor might write, and many no editor would (ROADMAP 5.3b, ADR-0064): 10,000 random
/// templates, built from the grammar in every context and then broken by chance, are parsed and rendered over random
/// values with each culture of merge-tags.json. None throws, each takes at most 50 ms (the fastest of three runs, so
/// that a garbage collection or the first compilation of a path does not count against it), the output keeps its limit,
/// the problems come in the order of the template, and rendering adds only the problems values cause. The same in
/// TypeScript (<c>packages/logic/test/templates.test.ts</c>).
/// </summary>
public sealed class TemplateFuzzTests
{
    private const int Templates = 10_000;
    private const int Seed = 5302;
    private const int Runs = 3;

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(50);

    /// <summary>What a template can come to: no problem, or each problem.</summary>
    private static readonly string[] Outcomes =
    [
        "clean", TemplateProblems.MalformedTag, TemplateProblems.UnclosedTag, TemplateProblems.TagInMarkup, TemplateProblems.TagInEscape,
        TemplateProblems.TagBeforePath, TemplateProblems.InvalidArgument, TemplateProblems.FilterNotForContext,
        TemplateProblems.FilterAfterEncoding, TemplateProblems.TooManyTags, TemplateProblems.TooManyFilters, TemplateProblems.TooLong,
        TemplateProblems.UnknownPath, TemplateProblems.UnknownFilter,
    ];

    [Test]
    public void TenThousandRandomTemplates_RenderWithoutThrowing_EachInAtMost50MillisecondsWithinTheLimitAndInOrder()
    {
        var generator = new TemplateGenerator(Seed);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        var slowest = (Time: TimeSpan.Zero, Template: string.Empty);
        for (var i = 0; i < Templates; i++)
        {
            var (text, context) = generator.Template();
            var values = generator.Values();
            var culture = generator.Culture();
            (ParsedTemplate Parsed, RenderedTemplate Rendered) Once()
            {
                var parsed = MergeTags.Parse(text, context);
                return (parsed, MergeTags.Render(parsed, path => values.GetValueOrDefault(path), culture));
            }

            var fastest = TimeSpan.MaxValue;
            (ParsedTemplate Parsed, RenderedTemplate Rendered) result;
            try
            {
                result = Once();
                for (var run = 0; run < Runs; run++)
                {
                    var watch = Stopwatch.StartNew();
                    Once();
                    watch.Stop();
                    fastest = watch.Elapsed < fastest ? watch.Elapsed : fastest;
                }
            }
            catch (Exception exception)
            {
                failures.Add($"template {i} in {context}: {exception.GetType().Name} for {Fuzzing.Show(text)}");
                continue;
            }

            if (Broken(text, result.Parsed, result.Rendered) is { } wrong)
            {
                failures.Add($"template {i} in {context}: {wrong} for {Fuzzing.Show(text)}");
            }

            var codes = result.Rendered.Problems.Select(problem => problem.Code).Distinct(StringComparer.Ordinal).ToList();
            IEnumerable<string> reached = codes.Count == 0 ? ["clean"] : codes;
            foreach (var code in reached)
            {
                outcomes[code] = outcomes.GetValueOrDefault(code) + 1;
            }

            if (fastest > Budget)
            {
                failures.Add($"template {i} in {context}: {fastest.TotalMilliseconds:F1} ms for {Fuzzing.Show(text)}");
            }

            if (fastest > slowest.Time)
            {
                slowest = (fastest, text);
            }
        }

        TestContext.Out.WriteLine(string.Join(", ", outcomes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}")));
        TestContext.Out.WriteLine($"slowest: {slowest.Time.TotalMilliseconds:F3} ms for {Fuzzing.Show(slowest.Template)}");
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);

            // The templates reach every outcome, so the generator explores the language and not one corner of it.
            Assert.That(Outcomes.Where(expected => outcomes.GetValueOrDefault(expected) < Templates / 500), Is.Empty, "outcomes reached fewer than 20 times");
        });
    }

    [Test]
    public void TheGenerator_GivesTheSameTemplatesForTheSameSeed()
    {
        var first = new TemplateGenerator(Seed);
        var second = new TemplateGenerator(Seed);

        Assert.That(Enumerable.Range(0, 100).Select(_ => first.Template()), Is.EqualTo(Enumerable.Range(0, 100).Select(_ => second.Template())));
    }

    [Test]
    public void MergeTags_RefuseNoArguments()
    {
        var parsed = MergeTags.Parse("{{ field.name }}", TemplateContext.Text);
        var culture = Fuzzing.TemplateCultures[0];

        Assert.Multiple(() =>
        {
            Assert.That(() => MergeTags.Parse(null!, TemplateContext.Text), Throws.ArgumentNullException);
            Assert.That(() => MergeTags.Render(null!, _ => null, culture), Throws.ArgumentNullException);
            Assert.That(() => MergeTags.Render(parsed, null!, culture), Throws.ArgumentNullException);
            Assert.That(() => MergeTags.Render(parsed, _ => null, null!), Throws.ArgumentNullException);
        });
    }

    /// <summary>What is wrong with a rendering, or null: the output's limit, the problems' order, and the problems parsing found.</summary>
    private static string? Broken(string text, ParsedTemplate parsed, RenderedTemplate rendered)
    {
        if (rendered.Text.Length > MergeTags.MaxOutput)
        {
            return $"{rendered.Text.Length} code units of output";
        }

        for (var k = 0; k < rendered.Problems.Count; k++)
        {
            var at = rendered.Problems[k].At;
            if (at < 0 || at > text.Length || (k > 0 && at < rendered.Problems[k - 1].At))
            {
                return "problems out of order or outside the template: " + Describe(rendered.Problems);
            }
        }

        // Until a limit stops it, rendering reports what parsing found, and beside it only the paths no source knows.
        if (rendered.Problems.All(problem => problem.Code != TemplateProblems.TooLong))
        {
            var found = rendered.Problems.Where(problem => problem.Code != TemplateProblems.UnknownPath).ToList();
            if (!found.SequenceEqual(parsed.Problems))
            {
                return $"rendering reported {Describe(found)}, parsing {Describe(parsed.Problems)}";
            }
        }

        return null;
    }

    private static string Describe(IEnumerable<TemplateProblem> problems) =>
        "[" + string.Join(", ", problems.Select(problem => $"{problem.Code} at {problem.At}")) + "]";

    /// <summary>Random templates from the grammar of ADR-0064 in every context, broken now and then, and random values for them.</summary>
    private sealed class TemplateGenerator(int seed)
    {
        private static readonly string Bs = new((char)92, 1);
        private static readonly string Quote = new((char)34, 1);
        private static readonly string Cr = new((char)13, 1);
        private static readonly string Lf = new((char)10, 1);
        private static readonly string Tab = new((char)9, 1);
        private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);

        private static readonly TemplateContext[] Contexts = Enum.GetValues<TemplateContext>();

        // The paths values come from; an unknown one now and then, and heads the grammar refuses.
        private static readonly string[] Paths =
        [
            "field.name", "field.qty", "field.birth", "field.when", "field.extras", "field.empty", "field.long", "query.ref", "form.title", "entry.number",
        ];

        private static readonly string[] UnknownPaths = ["nope.x", "field.missing", "field.Name"];
        private static readonly string[] OddHeads = ["Field", "1", ".x", "field.", "field..x", "-x", string.Empty, "field.$", "field name"];
        private static readonly string[] Literals = [Q("x"), Q("a " + Bs + Quote + " b"), Q("}}"), Q("{{"), Q(string.Empty), Q(Bs + Bs), Q("<b>&"), Quote + "open"];

        /// <summary>Each filter's arguments: those it takes, and some it refuses.</summary>
        private static readonly (string Name, string[][] Arguments)[] FilterArguments =
        [
            ("default", [[Q("web")], [Q(string.Empty)], [Q("<x>")], [], ["1"], [Q("a"), Q("b")]]),
            ("upper", [[], [], [], [Q("x")]]),
            ("lower", [[], [], [], ["1"]]),
            ("truncate", [["5"], ["0"], ["1", Q("...")], ["100000"], ["100001"], ["1.5"], ["-1"], [Q("5")], [], ["3", "4"]]),
            ("date", [[], [Q("short")], [Q("medium")], [Q("long")], [Q("time")], [Q("dateTime")], [Q("iso")], [Q("iso8601")], ["1"]]),
            ("number", [[], ["0"], ["2"], ["20"], ["21"], [Q("2")], ["2", "3"]]),
            ("currency", [[Q("EUR")], [Q("CHF")], [Q("JPY"), "0"], [Q("KWD")], [Q("eur")], [Q("EURO")], ["1"], [], [Q("EUR"), "21"]]),
            ("join", [[], [Q("; ")], [Q("</p>")], ["1"]]),
            ("html", [[], [], [], [Q("x")]]),
            ("url", [[], [], [], ["1"]]),
            ("json", [[], [], [], [Q(string.Empty)]]),
        ];

        private static readonly string[] OddFilters = ["shout", "Upper", "default2", "escape"];
        private static readonly string[][] OddArguments = [[], ["1"], [Q("x")]];
        private static readonly string[] Spaces = [" ", " ", string.Empty, string.Empty, "  ", Tab];
        private static readonly int[] FilterCounts = [0, 0, 1, 1, 1, 2, 2, 3];

        private static readonly string[] Words = ["Hello ", "a", " ", "Dear ", "!", ".", "x1"];

        // The characters of every context's syntax, so that tags land in each kind of place.
        private static readonly string[] Snippets =
        [
            "<p>", "</p>", "<a href=" + Quote, Quote + ">", "'", Quote, "<script>", "</script>", "<style>", "<!--", "-->", "<![CDATA[", "]]>", "<?x ",
            "?>", "<!DOCTYPE x>", "<br/>", "<", ">", "&amp;", "<textarea>", "</TEXTAREA>", "<title>", "</title>", "<plaintext>",
            "{" + Q("a") + ": ", Q("b"), Quote + Bs, Bs, "}", "[", "]", ",", ": ",
            "https://", "http://", "x.test", "/path", "?q=", "#f", "javascript:", "//", "mailto:", "&",
            Cr + Lf, Lf, Tab, "{", "}", "{{", "}}", "|", new((char)0xE9, 1), Emoji, new((char)0xD800, 1), new((char)0, 1),
        ];

        // What a mistake puts into a template: the grammar's own characters, and some it does not have.
        private static readonly string[] Noise =
        [
            "{", "}", "|", Quote, ":", ",", ".", " ", "-", "_", "<", ">", "/", "'", "a", "b", "Z", "0", "9", Bs, Cr, Lf,
            new((char)0xE9, 1), new((char)0xD800, 1), new((char)0, 1),
        ];

        private static readonly MergeValue[] Texts =
        [
            V("Anna"), V(string.Empty), V("  "), V("<b>" + Q("x") + "</b>"), V("O'Brien & Co"), V("a" + Cr + Lf + "b"), V("https://evil.test/?a=1&b=2"),
            V(new string((char)0xD800, 1)), V(Emoji + " " + (char)0x2028), V("{{ field.name }}"), V(new string('x', 3000)),
            V((char)0x130 + "stanbul " + (char)0x1F80), V("Stra" + (char)0xDF + "e"),
        ];

        private static readonly MergeValue[] Numbers =
        [
            N("1,234.5", "1234.5"), N("-0.5", "-0.5"), N("x", "1e5"), N("12", new string('9', 40)), V("42"), V("-7.125"), V("abc"),
            V("0.000000000000000000005"), V("-0"),
        ];

        private static readonly MergeValue[] Dates =
        [
            D("15.06.2026", "2026-06-15"), D("x", "2026-02-30"), V("2026-06-15"), V("2026-06-15T08:05:09"), V("23:59:59"), V("2026-06-15T24:00"),
            V("yesterday"), V("0001-01-01"), V("9999-12-31T23:59:59"),
        ];

        private static readonly MergeValue[] Lists =
        [
            L(), L(I("a"), I("b")), L(I("<i>"), I(Q("q")), I(string.Empty)), L(new MergeItem("1", Number: "1"), I("2")), L(I(string.Empty)),
            L(new MergeItem("x", Date: "2026-01-01")),
        ];

        // Long enough that two or three of them pass the output's limit, one ending in a surrogate pair.
        private static readonly MergeValue[] Longs = [V(new string('x', 34_000)), V(new string('y', 50_000)), V(new string('z', 69_998) + Emoji)];

        private static readonly Dictionary<string, MergeValue[]> Families = new(StringComparer.Ordinal)
        {
            ["field.name"] = Texts,
            ["field.qty"] = Numbers,
            ["field.birth"] = Dates,
            ["field.when"] = Dates,
            ["field.extras"] = Lists,
            ["field.empty"] = [V(string.Empty), L(), L(I(string.Empty))],
            ["field.long"] = Longs,
            ["query.ref"] = Texts,
            ["form.title"] = Texts,
            ["entry.number"] = Numbers,
        };

        private static readonly MergeValue[][] AnyFamily = [Texts, Numbers, Dates, Lists];

        private readonly Random _random = new(seed);

        public (string Text, TemplateContext Context) Template()
        {
            var context = Pick(Contexts);
            var text = _random.Next(50) switch
            {
                // As many tags as a template may have, a little fewer or more.
                0 => string.Join(" ", Enumerable.Repeat("{{ field.name }}", MergeTags.MaxTags + _random.Next(-1, 3))),

                // As many filters as a tag may have, a little fewer or more.
                1 => "{{ field.name" + string.Concat(Enumerable.Repeat(" | upper", MergeTags.MaxFilters + _random.Next(-1, 3))) + " }}",

                // Output about as long as a template may give.
                2 => _random.Next(2) == 0
                    ? new string('a', MergeTags.MaxOutput - _random.Next(10)) + "{{ field.name }}"
                    : string.Concat(Enumerable.Repeat("{{ field.long }}", _random.Next(2, 4))),
                _ => Built(),
            };

            // Then, now and then, broken: characters dropped, put in or doubled.
            var mistakes = _random.Next(6) == 0 ? _random.Next(1, 4) : 0;
            for (var m = 0; m < mistakes; m++)
            {
                text = Break(text);
            }

            return (text, context);
        }

        /// <summary>A value for each path, or none now and then: mostly of the path's kind, sometimes of any.</summary>
        public Dictionary<string, MergeValue> Values()
        {
            var values = new Dictionary<string, MergeValue>(StringComparer.Ordinal);
            foreach (var path in Paths)
            {
                if (_random.Next(8) == 0)
                {
                    continue;
                }

                var family = path != "field.long" && _random.Next(10) == 0 ? Pick(AnyFamily) : Families[path];
                values[path] = Pick(family);
            }

            return values;
        }

        public CultureFormats Culture() => Fuzzing.TemplateCultures[_random.Next(Fuzzing.TemplateCultures.Count)];

        private static string Q(string inner) => Quote + inner + Quote;

        private static MergeValue V(string text) => MergeValue.Of(text);

        private static MergeValue N(string text, string number) => MergeValue.Of(new MergeItem(text, Number: number));

        private static MergeValue D(string text, string date) => MergeValue.Of(new MergeItem(text, Date: date));

        private static MergeItem I(string text) => new(text);

        private static MergeValue L(params MergeItem[] items) => MergeValue.ListOf(items);

        private string Built()
        {
            var text = string.Empty;
            var pieces = _random.Next(1, 30);
            for (var p = 0; p < pieces; p++)
            {
                var k = _random.Next(10);
                text += k < 4 ? Tag() : k < 7 ? Pick(Words) : Pick(Snippets);
            }

            return text;
        }

        private string Tag()
        {
            var k = _random.Next(20);
            var head = k < 14 ? Pick(Paths) : k < 16 ? Pick(UnknownPaths) : k < 19 ? Pick(Literals) : Pick(OddHeads);
            var count = _random.Next(40) == 0 ? _random.Next(4, 13) : Pick(FilterCounts);
            var filters = string.Empty;
            for (var f = 0; f < count; f++)
            {
                var (name, arguments) = _random.Next(25) == 0 ? (Pick(OddFilters), OddArguments) : Pick(FilterArguments);
                var args = Pick(arguments);
                filters += Space() + "|" + Space() + name;
                if (args.Length > 0)
                {
                    filters += Space() + ":" + Space() + string.Join("," + Space(), args);
                }
            }

            return "{{" + Space() + head + filters + Space() + "}}";
        }

        private string Space() => Pick(Spaces);

        private string Break(string text)
        {
            var at = _random.Next(text.Length + 1);
            return _random.Next(3) switch
            {
                0 when text.Length > 0 => text.Remove(Math.Min(at, text.Length - 1), 1),
                0 => text,
                1 => text.Insert(at, Pick(Noise)),
                _ => text.Insert(at, text[at..Math.Min(text.Length, at + _random.Next(1, 8))]),
            };
        }

        private T Pick<T>(T[] items) => items[_random.Next(items.Length)];
    }
}
