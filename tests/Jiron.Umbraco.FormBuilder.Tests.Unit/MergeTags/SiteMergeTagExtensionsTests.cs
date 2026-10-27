using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// A site's own merge-tag sources and filters (ADR-0065): added through the interfaces and the collections, escaped by
/// the context like every value, empty and logged without values when they fail, and refused at start-up when a name is
/// taken or outside the grammar, naming the type.
/// </summary>
public sealed class SiteMergeTagExtensionsTests
{
    private const string Secret = "visitor@example.org";

    [Test]
    public async Task ASitesSource_GetsThePathAfterItsName_AndWhatTheTemplateRendersFor_AndItsValueIsEscaped()
    {
        var source = new CrmSource(_ => MergeTagValue.Of("<gold>"));
        var scope = Scope();

        var rendered = await Create([source]).Service.RenderAsync("{{ crm.segment.name }}", MergeTagPlace.Outcome, MergeTagContext.Html, scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("&lt;gold&gt;"));
            Assert.That(source.Requests, Is.EqualTo(new[] { new MergeTagSourceRequest("segment.name", scope, MergeTagPlace.Outcome, MergeTagContext.Html) }));
        });
    }

    [Test]
    public async Task ASitesSource_NamedAlone_GetsTheEmptyPath_AndMayGiveAList()
    {
        var source = new CrmSource(_ => MergeTagValue.ListOf([new MergeTagItem("a"), new MergeTagItem("b")]));

        var rendered = await Create([source]).RenderAsync("{{ crm }}");

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("a, b"));
            Assert.That(source.Requests.Single().Path, Is.Empty);
        });
    }

    [Test]
    public async Task APathTheSourceDoesNotKnow_RendersEmpty_WithAWarning()
    {
        var rendered = await Create([new CrmSource(_ => null)]).RenderAsync("[{{ crm.nothing }}]");

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[]"));
            Assert.That(rendered.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 4) }));
        });
    }

    [Test]
    public async Task ASourceThatThrows_RendersEmpty_AndIsLogged_WithItsTypeAndStack_ButNotItsMessage()
    {
        var site = Create([new CrmSource(_ => throw new InvalidOperationException(Secret + " is not in the CRM."))]);

        var rendered = await site.RenderAsync("[{{ crm.segment }}] {{ form.alias }}");

        var entry = site.Log.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[] contact"));
            Assert.That(rendered.Problems, Is.Empty);
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(entry.Message, Does.Contain("'crm'").And.Contain(typeof(InvalidOperationException).FullName).And.Contain(nameof(CrmSource)));
            Assert.That(entry.Message, Does.Not.Contain(Secret));
            Assert.That(entry.Exception, Is.Null, "the exception, whose message may hold an answer, is not logged");
        });
    }

    [Test]
    public void ACancelledRendering_StopsAtTheSource()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var service = Create([new CrmSource(_ => throw new OperationCanceledException())]).Service;

        Assert.That(() => service.RenderAsync("{{ crm.segment }}", MergeTagPlace.Mail, MergeTagContext.Text, Scope(), cancel.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ASourceThatTimesOutOnItsOwn_RendersEmpty_AndIsLogged()
    {
        var site = Create([new CrmSource(_ => throw new TaskCanceledException("The CRM did not answer."))]);

        var rendered = await site.RenderAsync("[{{ crm.segment }}]");

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[]"));
            Assert.That(site.Log.Entries.Single().Message, Does.Contain(typeof(TaskCanceledException).FullName));
        });
    }

    [Test]
    public async Task ASitesFilter_GetsTheValue_ItsArgumentsAndTheCulture_AndItsValueIsEscaped()
    {
        var filter = new RepeatFilter();
        var site = Create(filters: [filter]);

        var rendered = await site.Service.RenderAsync("{{ form.name | repeat: 2 }}", MergeTagPlace.Mail, MergeTagContext.Html, Scope(culture: "de-CH"));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Contact &lt;us&gt;Contact &lt;us&gt;"));
            Assert.That(filter.Requests.Single().Arguments, Is.EqualTo(new[] { new MergeTagArgument("2", IsString: false) }));
            Assert.That(filter.Requests.Single().Culture, Is.EqualTo("de-CH"));
            Assert.That(filter.Requests.Single().Value.Items.Single().Text, Is.EqualTo("Contact <us>"));
        });
    }

    [Test]
    public async Task ASitesFilter_RunsBesideTheBuiltInOnes_AndNotAfterAnEncodingOne()
    {
        var site = Create(filters: [new RepeatFilter()]);

        var chained = await site.RenderAsync("{{ form.alias | upper | repeat: 2 | truncate: 9 }}");
        var encoded = await site.Service.RenderAsync("{{ form.alias | html | repeat: 2 }}", MergeTagPlace.Mail, MergeTagContext.Html, Scope());

        Assert.Multiple(() =>
        {
            Assert.That(chained.Text, Is.EqualTo("CONTACTCO" + new string((char)0x2026, 1)));
            Assert.That(encoded.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.FilterAfterEncoding }));
        });
    }

    [TestCase("{{ form.alias | repeat: \"two\" }}", 16)]
    [TestCase("{{ form.alias | repeat }}", 16)]
    [TestCase("{{ form.alias | repeat: 1, 2 }}", 16)]
    public async Task ArgumentsTheFilterDoesNotAccept_AreInvalid_AtTheFilter_AndTheTagIsWritten(string template, int at)
    {
        var rendered = await Create(filters: [new RepeatFilter()]).RenderAsync(template);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(template));
            Assert.That(rendered.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.InvalidArgument, at) }));
        });
    }

    [Test]
    public async Task AFilterThatThrows_RendersEmpty_AndOneWhoseCheckThrows_TakesNoArguments_BothLoggedWithoutTheirMessages()
    {
        var site = Create(filters: [new ThrowingFilter("broken", inAccepts: false), new ThrowingFilter("picky", inAccepts: true)]);

        var applied = await site.RenderAsync("[{{ form.alias | broken }}]");
        var checkedOnly = await site.RenderAsync("[{{ form.alias | picky }}]");

        Assert.Multiple(() =>
        {
            Assert.That(applied.Text, Is.EqualTo("[]"));
            Assert.That(applied.Problems, Is.Empty);
            Assert.That(checkedOnly.Text, Is.EqualTo("[{{ form.alias | picky }}]"));
            Assert.That(checkedOnly.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.InvalidArgument }));
            Assert.That(site.Log.Entries.Select(entry => entry.Message), Has.Exactly(1).Contains("'broken'").And.Exactly(1).Contains("'picky'"));
            Assert.That(site.Log.Entries.Select(entry => entry.Message), Has.All.Contains(typeof(FormatException).FullName).And.All.Not.Contains(Secret));
        });
    }

    [Test]
    public async Task AFilterThatGivesNothing_RendersEmpty()
    {
        var rendered = await Create(filters: [new NothingFilter()]).RenderAsync("[{{ form.alias | nothing }}]");

        Assert.That(rendered.Text, Is.EqualTo("[]"));
    }

    [Test]
    public void TheCheck_WarnsOfPathsNoSourceKnows_BeforeAnyValue_InTheTemplatesOrder()
    {
        var service = Create([new CrmSource(_ => null)]).Service;
        const string Template = "{{ nobody.at }} {{ crm.anything }} {{ entry.colour }} {{ context.anything }} {{ query.secret }} {{ query.tag }} {{ form.name | nope }}";

        var problems = service.Check(Template, MergeTagPlace.Mail, MergeTagContext.Text, Form(query: ["tag"]));

        Assert.That(problems, Is.EqualTo(new[]
        {
            new MergeTagProblem(MergeTagProblems.UnknownPath, Template.IndexOf("nobody", StringComparison.Ordinal)),
            new MergeTagProblem(MergeTagProblems.UnknownPath, Template.IndexOf("entry.colour", StringComparison.Ordinal)),
            new MergeTagProblem(MergeTagProblems.UnknownPath, Template.IndexOf("query.secret", StringComparison.Ordinal)),
            new MergeTagProblem(MergeTagProblems.UnknownFilter, Template.IndexOf("nope", StringComparison.Ordinal)),
        }));
    }

    [Test]
    public void ThePaths_OfASourceThatFails_AreLeftOut_AndLogged()
    {
        var site = Create([new CrmSource(_ => null, pathsFail: true)]);

        var paths = site.Service.Paths(MergeTagPlace.Mail, Form());

        Assert.Multiple(() =>
        {
            Assert.That(paths.Select(path => path.Path), Does.Contain("form.name").And.Not.Contain("crm.segment"));
            Assert.That(site.Log.Entries.Single().Message, Does.Contain("'crm'").And.Not.Contain(Secret));
        });
    }

    [Test]
    public async Task ASitesSourceOrFilter_WithATakenName_IsLeftOut_SoTheBuiltInOneStays()
    {
        var site = Create([new NamedSource("entry"), new NamedSource("Crm")], [new NamedFilter("upper"), new NamedFilter("not-a-name")]);

        var rendered = await site.RenderAsync("{{ entry.number }}|{{ form.alias | upper }}", Scope(entry: Entry(DateTimeOffset.UnixEpoch, number: 7)));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("7|CONTACT"));
            Assert.That(site.Service.Paths(MergeTagPlace.Mail, Form()).Select(path => path.Path), Has.None.StartsWith("Crm"));
        });
    }

    [Test]
    public void TheProblems_NameEachSourceOrFilter_WhoseNameIsTakenOrOutsideTheGrammar()
    {
        var problems = MergeTagRegistry.Problems(
            [new NamedSource("entry"), new NamedSource("Crm"), new NamedSource("crm-x"), new NamedSource(null!), new NamedSource("crm"), new CrmSource(_ => null), new NamedSource("crm2")],
            [new NamedFilter("upper"), new NamedFilter("1st"), new RepeatFilter(), new NamedFilter("repeat")]);

        Assert.That(problems, Is.EqualTo(new[]
        {
            $"The merge-tag source {typeof(NamedSource).FullName} is named 'entry', which a built-in source has.",
            $"The merge-tag source {typeof(NamedSource).FullName} is named 'Crm', which is not a name merge tags can use: a lowercase letter, then letters and digits.",
            $"The merge-tag source {typeof(NamedSource).FullName} is named 'crm-x', which is not a name merge tags can use: a lowercase letter, then letters and digits.",
            $"The merge-tag source {typeof(NamedSource).FullName} is named '', which is not a name merge tags can use: a lowercase letter, then letters and digits.",
            $"The merge-tag sources {typeof(NamedSource).FullName} and {typeof(CrmSource).FullName} are both named 'crm'; each needs a name of its own.",
            $"The merge-tag filter {typeof(NamedFilter).FullName} is named 'upper', which a built-in filter has.",
            $"The merge-tag filter {typeof(NamedFilter).FullName} is named '1st', which is not a name merge tags can use: a lowercase letter, then letters and digits.",
            $"The merge-tag filters {typeof(RepeatFilter).FullName} and {typeof(NamedFilter).FullName} are both named 'repeat'; each needs a name of its own.",
        }));
    }

    [Test]
    public void TheStartupCheck_PassesASiteWithoutAProblem_AndRefusesOneWithAProblem_NamingIt()
    {
        using var valid = Site(sources => sources.Append<CrmSource>(), filters => filters.Append<RepeatFilter>());
        using var taken = Site(sources => sources.Append<DateNamedSource>(), _ => { });
        using var broken = Site(sources => sources.Append<UnconfiguredSource>(), _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(() => HostOptions.Start(valid), Throws.Nothing);
            Assert.That(valid.GetRequiredService<IFormBuilderMergeTagService>().Paths(MergeTagPlace.Mail, Form()).Select(path => path.Path), Does.Contain("crm.segment"));
            Assert.That(valid.GetRequiredService<IFormBuilderMergeTagService>(), Is.SameAs(valid.GetRequiredService<IFormBuilderMergeTagService>()));
            Assert.That(() => HostOptions.Start(taken), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                $"The merge-tag source {typeof(DateNamedSource).FullName} is named 'date', which a built-in source has."));
            Assert.That(() => HostOptions.Start(broken), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                "Jiron Form Builder cannot create the site's merge-tag sources and filters: Set Crm:Url to the CRM's address."));
        });
    }

    // The services of a site whose composers added merge-tag sources and filters, as the core composer registers them,
    // with the clock, the settings, the field types and the logging every site has.
    private static ServiceProvider Site(Action<FormBuilderMergeTagSourceCollectionBuilder> sources, Action<FormBuilderMergeTagFilterCollectionBuilder> filters)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(HostOptions.Folders());
        services.AddSingleton(MergeTagTestSite.Settings("UTC"));
        services.AddSingleton(TestFieldTypes.Registry);
        new TestUmbraco().Register(services);
        services.AddFormBuilderOptions(HostOptions.Configuration());
        services.AddSingleton<IFormBuilderOptionService>(new OptionListTestSite().Options());
        var sourceBuilder = new FormBuilderMergeTagSourceCollectionBuilder();
        sources(sourceBuilder);
        sourceBuilder.RegisterWith(services);
        var filterBuilder = new FormBuilderMergeTagFilterCollectionBuilder();
        filters(filterBuilder);
        filterBuilder.RegisterWith(services);
        services.AddMergeTagService();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>A site's source of CRM segments, per visitor, which keeps the requests it got.</summary>
    public sealed class CrmSource : IFormBuilderMergeTagSource
    {
        private readonly Func<MergeTagSourceRequest, MergeTagValue?> _resolve;
        private readonly bool _pathsFail;
        private readonly List<MergeTagSourceRequest> _requests = [];

        /// <summary>The source as a composer adds it: every path gives <c>gold</c>.</summary>
        public CrmSource()
            : this(_ => MergeTagValue.Of("gold"))
        {
        }

        internal CrmSource(Func<MergeTagSourceRequest, MergeTagValue?> resolve, bool pathsFail = false)
        {
            _resolve = resolve;
            _pathsFail = pathsFail;
        }

        public IReadOnlyList<MergeTagSourceRequest> Requests => _requests;

        public string Name => "crm";

        public bool IsPerVisitor => true;

        public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) =>
            _pathsFail ? throw new InvalidOperationException(Secret) : [new MergeTagPathInfo("crm.segment", "Segment")];

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return ValueTask.FromResult(_resolve(request));
        }
    }

    /// <summary>A site's source whose name is a built-in one's.</summary>
    public sealed class DateNamedSource : IFormBuilderMergeTagSource
    {
        public string Name => "date";

        public bool IsPerVisitor => false;

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<MergeTagValue?>(null);
    }

    /// <summary>A site's source that cannot be created until the site configures it.</summary>
    public sealed class UnconfiguredSource : IFormBuilderMergeTagSource
    {
        public UnconfiguredSource() => throw new InvalidOperationException("Set Crm:Url to the CRM's address.");

        public string Name => "crm";

        public bool IsPerVisitor => true;

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<MergeTagValue?>(null);
    }

    /// <summary>A source with any name, which gives <c>named</c>.</summary>
    private sealed class NamedSource(string name) : IFormBuilderMergeTagSource
    {
        public string Name => name;

        public bool IsPerVisitor => false;

        public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [new(name + ".path")];

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<MergeTagValue?>(MergeTagValue.Of("named"));
    }

    /// <summary>A site's filter that repeats each item as often as its one number argument says, which keeps its requests.</summary>
    public sealed class RepeatFilter : IFormBuilderMergeTagFilter
    {
        private readonly List<MergeTagFilterRequest> _requests = [];

        public IReadOnlyList<MergeTagFilterRequest> Requests => _requests;

        public string Name => "repeat";

        public bool Accepts(IReadOnlyList<MergeTagArgument> arguments) => arguments is [{ IsString: false } count] && int.TryParse(count.Text, out var times) && times is > 0 and < 10;

        public MergeTagValue Apply(MergeTagFilterRequest request)
        {
            _requests.Add(request);
            var times = int.Parse(request.Arguments[0].Text, System.Globalization.CultureInfo.InvariantCulture);
            return new MergeTagValue([.. request.Value.Items.Select(item => new MergeTagItem(string.Concat(Enumerable.Repeat(item.Text, times))))], request.Value.IsList);
        }
    }

    /// <summary>A filter with any name, which changes nothing.</summary>
    private sealed class NamedFilter(string name) : IFormBuilderMergeTagFilter
    {
        public string Name => name;

        public bool Accepts(IReadOnlyList<MergeTagArgument> arguments) => true;

        public MergeTagValue Apply(MergeTagFilterRequest request) => request.Value;
    }

    /// <summary>A filter that throws as it checks its arguments or as it applies, with an answer in the message.</summary>
    private sealed class ThrowingFilter(string name, bool inAccepts) : IFormBuilderMergeTagFilter
    {
        public string Name => name;

        public bool Accepts(IReadOnlyList<MergeTagArgument> arguments) => inAccepts ? throw new FormatException(Secret) : true;

        public MergeTagValue Apply(MergeTagFilterRequest request) => throw new FormatException(Secret);
    }

    /// <summary>A filter that breaks its contract and gives no value.</summary>
    private sealed class NothingFilter : IFormBuilderMergeTagFilter
    {
        public string Name => "nothing";

        public bool Accepts(IReadOnlyList<MergeTagArgument> arguments) => true;

        public MergeTagValue Apply(MergeTagFilterRequest request) => null!;
    }
}
