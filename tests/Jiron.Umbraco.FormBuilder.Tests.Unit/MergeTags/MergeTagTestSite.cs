using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// A site for the merge-tag tests (ADR-0065): the service over the built-in sources and the site's own, a form whose
/// settings the tests choose, the scope a template renders for, and the log of the sources and filters that failed.
/// The Umbraco sources read a <see cref="TestUmbraco"/>, empty unless the test gives one (ADR-0067).
/// </summary>
internal static class MergeTagTestSite
{
    /// <summary>The site's root content.</summary>
    public static readonly Guid SiteKey = Guid.Parse("50000000-0000-4000-8000-000000000001");

    /// <summary>A form with the entry reference format and the allowed query and context names.</summary>
    public static FormDefinition Form(EntryReferenceFormat? reference = null, IReadOnlyList<string>? query = null, IReadOnlyList<string>? context = null) => new()
    {
        Key = Guid.Parse("f0000000-0000-4000-8000-000000000001"),
        Settings = new FormSettings
        {
            Submission = new FormSubmissionSettings { EntryReference = reference },
            Developer = new FormDeveloperSettings { QueryValues = query ?? [], ContextValues = context ?? [] },
        },
    };

    /// <summary>What a template renders for: the form named <c>Contact &lt;us&gt;</c>, in British English, on the site.</summary>
    public static MergeTagScope Scope(
        FormDefinition? form = null,
        MergeTagEntry? entry = null,
        string culture = "en-GB",
        IReadOnlyDictionary<string, IReadOnlyList<string>>? query = null,
        IReadOnlyDictionary<string, string>? context = null,
        MergeTagAnswers? answers = null,
        IReadOnlySet<Guid>? includedSensitive = null,
        Guid? page = null,
        Guid? member = null) => new()
        {
            Form = form ?? Form(),
            FormName = "Contact <us>",
            FormAlias = "contact",
            Culture = culture,
            Site = SiteKey,
            Page = page,
            Member = member,
            Entry = entry,
            Query = query!,
            Context = context!,
            Answers = answers!,
            IncludedSensitive = includedSensitive!,
        };

    /// <summary>An entry submitted at an instant.</summary>
    public static MergeTagEntry Entry(DateTimeOffset submitted, int number = 42, string state = "submitted") => new()
    {
        Key = Guid.Parse("e0000000-0000-4000-8000-000000000001"),
        Number = number,
        State = state,
        Submitted = submitted,
    };

    /// <summary>
    /// The service of a site with its own sources and filters, whose time zone is <paramref name="zone"/> and whose
    /// general settings give <paramref name="baseUrl"/>, on a clock that starts at 2026-01-01 00:00 UTC unless the test
    /// gives one, over the Umbraco the test gives, printing with .NET's culture data unless the test gives formats.
    /// </summary>
    public static Site Create(
        IEnumerable<IFormBuilderMergeTagSource>? sources = null,
        IEnumerable<IFormBuilderMergeTagFilter>? filters = null,
        string zone = "UTC",
        TimeProvider? time = null,
        FieldTypeRegistry? types = null,
        TestUmbraco? umbraco = null,
        string? baseUrl = null,
        MergeTagCultures? cultures = null)
    {
        umbraco ??= new TestUmbraco();
        var settings = Settings(zone, baseUrl);
        var clock = new MergeTagClock(settings, time ?? new TestTimeProvider());
        cultures ??= new MergeTagCultures();
        var log = new RecordingLogger();
        var dictionary = umbraco.DictionaryTexts();
        var answers = new MergeTagAnswerTexts(types ?? TestFieldTypes.Registry, cultures, dictionary, log);
        var registry = new MergeTagRegistry(
            FormBuilderMergeTagServices.BuiltInSources(clock, cultures, answers, umbraco.Umbraco(), umbraco.BaseUrls(settings), dictionary),
            new FormBuilderMergeTagSourceCollection(() => sources ?? []),
            new FormBuilderMergeTagFilterCollection(() => filters ?? []));
        return new Site(new FormBuilderMergeTagService(registry, cultures, log), log);
    }

    /// <summary>The general settings of a site whose time zone is <paramref name="zone"/> and whose base URL is <paramref name="baseUrl"/>.</summary>
    public static IFormBuilderSettingsService Settings(string zone, string? baseUrl = null)
    {
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<GeneralSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            new SettingsSnapshot<GeneralSettings>(new GeneralSettings { TimeZone = zone, BaseUrl = baseUrl }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        return settings;
    }

    /// <summary>A site's service and the log of its sources and filters.</summary>
    /// <param name="Service">The service.</param>
    /// <param name="Log">What the service logged.</param>
    public sealed record Site(IFormBuilderMergeTagService Service, RecordingLogger Log)
    {
        /// <summary>A template rendered in a place, a mail's text unless the test names another.</summary>
        public Task<MergeTagRendering> RenderAsync(string text, MergeTagScope? scope = null, MergeTagPlace place = MergeTagPlace.Mail, MergeTagContext context = MergeTagContext.Text) =>
            Service.RenderAsync(text, place, context, scope ?? MergeTagTestSite.Scope());
    }

    /// <summary>A logger that keeps what is logged, with the exception passed, if any.</summary>
    public sealed class RecordingLogger : ILogger<FormBuilderMergeTagService>, ILogger<MergeTagAnswerTexts>
    {
        private readonly Lock _lock = new();
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

        /// <summary>The entries so far.</summary>
        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                _entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
