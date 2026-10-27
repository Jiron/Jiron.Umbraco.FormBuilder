using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists.OptionListTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// Resolving options (ADR-0073): a field's own options and a manual list's in the culture with their fallback and
/// dictionary items, a source on a field or a list, a cascading field's options for the parent's answers, what a source
/// gives cleaned as entries need it, a failing or slow source unavailable and logged without values, and lists kept
/// until they change.
/// </summary>
[TestFixture]
public sealed class OptionServiceTests
{
    private static readonly Guid ListKey = Guid.Parse("73000000-0000-4000-8000-000000000010");

    private static readonly Guid OtherListKey = Guid.Parse("73000000-0000-4000-8000-000000000011");

    private static readonly Guid Page = Guid.Parse("73000000-0000-4000-8000-000000000020");

    [Test]
    public async Task AFieldsOwnOptions_AreInTheCulture_WithTheTextsFallback_AndDictionaryItemsInThatCulture()
    {
        var site = new OptionListTestSite();
        site.Umbraco.Language("en-US", isDefault: true);
        site.Umbraco.Language("de-DE");
        site.Umbraco.Language("de-CH", fallback: "de-DE");
        site.Umbraco.Item("fruit.banana", new Dictionary<string, string> { ["en-US"] = "Banana", ["de-DE"] = "Banane" });
        var field = Field(new FieldOptions
        {
            Items =
            [
                new FieldOption { Value = "apple", Label = Text("Apple", "de", "Apfel", "de-CH", "Opfel"), Description = Text("Red", "de", "Rot"), Places = 3 },
                new FieldOption { Value = "banana", Label = Text("#fruit.banana"), Image = Page },
                new FieldOption { Value = "cherry" },
            ],
        });
        var service = site.Options();

        var english = await service.GetAsync(field, new FormBuilderOptionContext("en-US"));
        var german = await service.GetAsync(field, new FormBuilderOptionContext("de-AT"));
        var swiss = await service.GetAsync(field, new FormBuilderOptionContext("de-CH"));
        var none = await service.GetAsync(Field(new FieldOptions()), new FormBuilderOptionContext("en"));
        var notAChoice = await service.GetAsync(new FormField { Key = FieldKey, Type = "text" }, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(english.IsAvailable, Is.True);
            Assert.That(Pairs(english), Is.EqualTo(new[] { ("apple", "Apple"), ("banana", "Banana"), ("cherry", (string?)null) }));
            Assert.That(Pairs(german), Is.EqualTo(new[] { ("apple", "Apfel"), ("banana", "Banana"), ("cherry", (string?)null) }), "de-AT's text falls back to de; the site has no language for it, so the item takes the default language's");
            Assert.That(Pairs(swiss), Is.EqualTo(new[] { ("apple", "Opfel"), ("banana", "Banane"), ("cherry", (string?)null) }), "de-CH's item falls back to de-DE");
            Assert.That(german.Options[0].Description, Is.EqualTo("Rot"));
            Assert.That((english.Options[0].Places, english.Options[1].Image, english.Options[2].Description), Is.EqualTo(((int?)3, (Guid?)Page, (string?)null)));
            Assert.That((none.IsAvailable, none.Options.Count), Is.EqualTo((true, 0)), "own options with none");
            Assert.That(notAChoice, Is.SameAs(FormBuilderResolvedOptions.None), "a field without options");
        });
    }

    [Test]
    public async Task AManualList_GivesItsItemsInTheCulture_KeptUntilItChanges_WhenItsNewOptionsShowAtOnce()
    {
        var site = new OptionListTestSite();
        var created = await site.Lists.CreateAsync(ListKey, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","label":{"":"Small","de":"Klein"}},{"value":"m","label":{"":"Medium"}}]"""));
        var service = site.Options();
        var field = Field(new FieldOptions { Source = OptionSource.List, OptionList = ListKey });

        var english = await service.GetAsync(field, new FormBuilderOptionContext("en"));
        var german = await service.GetListAsync(ListKey, new FormBuilderOptionContext("de-CH"));
        var reads = site.Repository.Reads;
        await site.Lists.UpdateAsync(ListKey, created.List!.ConcurrencyVersion, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"l","label":{"":"Large"}}]"""));
        var changed = await service.GetAsync(field, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(created.Succeeded, Is.True);
            Assert.That(Pairs(english), Is.EqualTo(new[] { ("s", "Small"), ("m", "Medium") }));
            Assert.That(Pairs(german), Is.EqualTo(new[] { ("s", "Klein"), ("m", "Medium") }));
            Assert.That(reads, Is.EqualTo(1), "the list is read once while it does not change");
            Assert.That(Pairs(changed), Is.EqualTo(new[] { ("l", "Large") }), "a changed list's options show at once");
            Assert.That(site.Told, Is.EqualTo(new[] { ListKey, ListKey }), "the other servers hear of each change");
        });
    }

    [Test]
    public async Task OptionsFromAList_AreUnavailable_ForAListTheSiteLacks_OrOneThatNoLongerReads_LoggedOnce()
    {
        var site = new OptionListTestSite();
        site.Repository.Write(ListKey, OptionListKind.Manual, items: """[{"value":1}]""");
        site.Repository.Write(OtherListKey, OptionListKind.Url, config: """{"url":"https://example.com"}""");
        var service = site.Options();

        var missing = await service.GetListAsync(Guid.NewGuid(), new FormBuilderOptionContext("en"));
        var unreadable = await service.GetAsync(Field(new FieldOptions { Source = OptionSource.List, OptionList = ListKey }), new FormBuilderOptionContext("en"));
        var again = await service.GetListAsync(ListKey, new FormBuilderOptionContext("de"));
        var later = await service.GetListAsync(OtherListKey, new FormBuilderOptionContext("en"));
        var unnamed = await service.GetAsync(Field(new FieldOptions { Source = OptionSource.List }), new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(new[] { missing, unreadable, again, later, unnamed }, Has.All.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.ListLogs.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Error, $"The option list {ListKey} does not read as its kind, so it offers no options until it is saved again. Problems: 1"),
                (LogLevel.Error, $"The option list {OtherListKey} does not read as its kind, so it offers no options until it is saved again. Problems: 1"),
            }), "once per list while it is kept, a kind of a later version among them");
        });
    }

    [Test]
    public async Task ACodeSource_FiltersACascadingFieldsOptions_ByTheParentsAnswers_AndAnyOtherFieldIgnoresThem()
    {
        var cities = new TestOptionSource(
            "cities",
            new FormBuilderOption("zurich", "Zurich") { ParentValues = ["CH"] },
            new FormBuilderOption("vienna", "Vienna") { ParentValues = ["AT", "EU"] },
            new FormBuilderOption("elsewhere", "Somewhere else"));
        var site = new OptionListTestSite(cities);
        var service = site.Options();
        var cascading = Field(OptionSource.Code, """{"source":"cities"}""", parent: ParentKey);
        var plain = Field(OptionSource.Code, """{"source":"cities"}""");

        var swiss = await service.GetAsync(cascading, new FormBuilderOptionContext("en") { ParentValues = ["CH"], Page = Page });
        var european = await service.GetAsync(cascading, new FormBuilderOptionContext("en") { ParentValues = ["EU", "CH"] });
        var unanswered = await service.GetAsync(cascading, new FormBuilderOptionContext("en"));
        var all = await service.GetAsync(plain, new FormBuilderOptionContext("en") { ParentValues = ["CH"] });

        Assert.Multiple(() =>
        {
            Assert.That(Values(swiss), Is.EqualTo(new[] { "zurich", "elsewhere" }));
            Assert.That(Values(european), Is.EqualTo(new[] { "zurich", "vienna", "elsewhere" }), "an option belongs to any of the answers");
            Assert.That(Values(unanswered), Is.EqualTo(new[] { "elsewhere" }), "without an answer only the options of every answer");
            Assert.That(Values(all), Is.EqualTo(new[] { "zurich", "vienna", "elsewhere" }));
            Assert.That(cities.Requests, Is.EqualTo(new[]
            {
                new FormBuilderOptionRequest("en", ["CH"], Page),
                new FormBuilderOptionRequest("en", ["EU", "CH"], null),
                new FormBuilderOptionRequest("en", [], null),
                new FormBuilderOptionRequest("en", [], null),
            }).Using<FormBuilderOptionRequest>(SameRequest), "the source hears the parent's answers only for a cascading field");
        });
    }

    [Test]
    public async Task ASourceThatThrows_GivesNoOptions_MarkedUnavailable_AndIsLoggedWithoutItsMessage([Values] bool synchronously)
    {
        var broken = new TestOptionSource("broken", async (request, _) =>
        {
            var secret = "JFB_FAKE answer " + request.Culture;
            if (synchronously)
            {
                throw new InvalidOperationException(secret);
            }

            await Task.Yield();
            throw new InvalidOperationException(secret);
        });
        var site = new OptionListTestSite(broken);
        var field = Field(OptionSource.Code, """{"source":"broken"}""");

        var resolved = await site.Options().GetAsync(field, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.Logs.Entries, Has.Count.EqualTo(1));
            Assert.That(site.Logs.Entries[0].Level, Is.EqualTo(LogLevel.Error));
            Assert.That(site.Logs.Entries[0].Message, Does.StartWith($"The code source of the field {FieldKey} failed with System.InvalidOperationException, so it offers no options."));
            Assert.That(site.Logs.Entries[0].Message, Does.Not.Contain("JFB_FAKE"), "the message may quote what the source read");
            Assert.That(site.Logs.Entries[0].Exception, Is.Null, "and so the exception is not passed either");
        });
    }

    [Test]
    public async Task ASourceThatTakesLongerThanFiveSeconds_GivesNoOptions_EvenOneThatDoesNotHeedItsToken([Values] bool heedsItsToken)
    {
        var never = new TaskCompletionSource<IReadOnlyList<FormBuilderOption>>();
        var slow = new TestOptionSource("slow", async (_, cancellationToken) =>
            heedsItsToken ? await never.Task.WaitAsync(cancellationToken) : await never.Task);
        var site = new OptionListTestSite(slow);
        var timers = new ManualTimers();
        var pending = site.Options(timers).GetAsync(Field(OptionSource.Code, """{"source":"slow"}"""), new FormBuilderOptionContext("en"));

        Assert.That(pending.IsCompleted, Is.False, "it waits");
        Assert.That(timers.Timers.Select(timer => timer.DueTime), Is.EqualTo(new[] { TimeSpan.FromSeconds(5) }));
        timers.FireAll();
        var resolved = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.Logs.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Error, $"The code source of the field {FieldKey} took longer than 5 seconds, so it offers no options"),
            }));
            Assert.That(timers.Timers.Single().Disposed, Is.True, "the timer ends with the request");
        });
    }

    [Test]
    public void ACancelledRequest_IsCancelled_NotAnUnavailableSource()
    {
        var slow = new TestOptionSource("slow", async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        });
        var site = new OptionListTestSite(slow);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await site.Options().GetAsync(Field(OptionSource.Code, """{"source":"slow"}"""), new FormBuilderOptionContext("en"), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(site.Logs.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task WhatASourceGives_IsCleanedAsEntriesNeedIt_AndWhatIsLeftOutIsLoggedAsACount()
    {
        var decomposed = "caf" + "e" + (char)0x301;
        var messy = new TestOptionSource(
            "messy",
            new FormBuilderOption("a", "A"),
            new FormBuilderOption(string.Empty, "Nothing"),
            new FormBuilderOption("   ", "Spaces"),
            null!,
            new FormBuilderOption(new string('x', 201), "Too long"),
            new FormBuilderOption(new string('y', 200), "Long enough"),
            new FormBuilderOption(" padded", "Padded"),
            new FormBuilderOption(decomposed, "Decomposed"),
            new FormBuilderOption("A", "Again"),
            new FormBuilderOption("b", "  "),
            new FormBuilderOption("c", string.Empty));
        var site = new OptionListTestSite(messy);

        var resolved = await site.Options().GetAsync(Field(OptionSource.Code, """{"source":"messy"}"""), new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.True);
            Assert.That(Pairs(resolved), Is.EqualTo(new (string, string?)[] { ("a", "A"), (new string('y', 200), "Long enough"), ("b", null), ("c", null) }), "a blank label shows the value");
            Assert.That(site.Logs.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Warning, $"The field {FieldKey} left out 7 options: blank, too long, not clean or repeated values, or more than 10,000"),
            }));
            Assert.That(site.Logs.Entries[0].Message, Does.Not.Contain("padded").And.Not.Contain("Again"), "never with values");
        });
    }

    [Test]
    public async Task ASourceGivesAtMostTenThousandOptions_AndNoneForNoList()
    {
        var many = new TestOptionSource("many", [.. Enumerable.Range(0, 10_002).Select(i => new FormBuilderOption("v" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
        var nothing = new TestOptionSource("nothing", (_, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(null!));
        var site = new OptionListTestSite(many, nothing);
        var service = site.Options();

        var capped = await service.GetAsync(Field(OptionSource.Code, """{"source":"many"}"""), new FormBuilderOptionContext("en"));
        var none = await service.GetAsync(Field(OptionSource.Code, """{"source":"nothing"}"""), new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(capped.Options, Has.Count.EqualTo(OptionListService.MaxOptions));
            Assert.That(capped.Options[^1].Value, Is.EqualTo("v9999"), "the first ones, in order");
            Assert.That(site.Logs.Entries.Single().Message, Does.Contain("left out 2 options"));
            Assert.That((none.IsAvailable, none.Options.Count), Is.EqualTo((true, 0)));
        });
    }

    [Test]
    public async Task TheCountries_OnAField_AreNamedInTheCulture()
    {
        var site = new OptionListTestSite();
        var field = Field(OptionSource.Countries, """{"countries":["LI","CH"]}""");

        var english = await site.Options().GetAsync(field, new FormBuilderOptionContext("en"));
        var german = await site.Options().GetAsync(field, new FormBuilderOptionContext("de"));

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(english), Is.EqualTo(new[] { ("LI", "Liechtenstein"), ("CH", "Switzerland") }));
            Assert.That(Pairs(german), Is.EqualTo(new[] { ("LI", "Liechtenstein"), ("CH", "Schweiz") }));
        });
    }

    [Test]
    public async Task ASourceOnAField_IsUnavailable_ForAKindTheSiteLacks_AndAnUploadIsNoFieldsSource()
    {
        var site = new OptionListTestSite();
        var service = site.Options();

        var url = await service.GetAsync(Field(OptionSource.Url, """{"url":"https://example.com"}"""), new FormBuilderOptionContext("en"));
        var file = await service.GetAsync(Field(OptionSource.File), new FormBuilderOptionContext("en"));

        Assert.That(new[] { url, file }, Has.All.SameAs(FormBuilderResolvedOptions.Unavailable));
    }

    [Test]
    public async Task ASourceList_IsKeptForItsCacheSeconds_PerCultureAndParentAnswers_AndDroppedWhenItChanges()
    {
        var counting = new TestOptionSource("counting", (request, _) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([new FormBuilderOption("one", request.Culture)]));
        var site = new OptionListTestSite(counting);
        var created = await site.Lists.CreateAsync(ListKey, new OptionListInput("kept", "Kept", OptionListKind.Code, Config: """{"source":"counting"}""", CacheSeconds: 60));
        await site.Lists.CreateAsync(OtherListKey, new OptionListInput("fresh", "Fresh", OptionListKind.Code, Config: """{"source":"counting"}"""));
        var service = site.Options();

        await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));
        var kept = await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));
        await service.GetListAsync(ListKey, new FormBuilderOptionContext("de"));
        await service.GetListAsync(ListKey, new FormBuilderOptionContext("de") { ParentValues = ["b", "a"] });
        await service.GetListAsync(ListKey, new FormBuilderOptionContext("de") { ParentValues = ["a", "b"] });
        await service.GetListAsync(ListKey, new FormBuilderOptionContext("de") { Page = Page });
        var before = counting.Requests.Count;
        await site.Lists.UpdateAsync(ListKey, created.List!.ConcurrencyVersion, new OptionListInput("kept", "Kept", OptionListKind.Code, Config: """{"source":"counting"}""", CacheSeconds: 120));
        await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));
        var afterChange = counting.Requests.Count;
        await service.GetListAsync(OtherListKey, new FormBuilderOptionContext("en"));
        await service.GetListAsync(OtherListKey, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(kept), Is.EqualTo(new[] { ("one", "en") }));
            Assert.That(before, Is.EqualTo(4), "one load per culture, page and set of parent answers in any order");
            Assert.That(afterChange, Is.EqualTo(5), "a change drops what was kept");
            Assert.That(counting.Requests.Count, Is.EqualTo(7), "a list with no time keeps nothing");
        });
    }

    [Test]
    public async Task AKeptSourceList_KeepsNoFailure_SoTheNextRequestAsksAgain()
    {
        var calls = 0;
        var flaky = new TestOptionSource("flaky", (_, _) => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("JFB_FAKE down")
            : ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([new FormBuilderOption("up")]));
        var site = new OptionListTestSite(flaky);
        await site.Lists.CreateAsync(ListKey, new OptionListInput("flaky", "Flaky", OptionListKind.Code, Config: """{"source":"flaky"}""", CacheSeconds: 600));
        var service = site.Options();

        var failed = await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));
        var recovered = await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));
        var kept = await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(Values(recovered), Is.EqualTo(new[] { "up" }));
            Assert.That(Values(kept), Is.EqualTo(new[] { "up" }));
            Assert.That(calls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TheRefresher_DropsAChangedList_OnTheServerItRunsOn_AndEveryListForAChangeItCannotRead()
    {
        var site = new OptionListTestSite();
        site.Repository.Write(ListKey, OptionListKind.Manual, items: """[{"value":"a"}]""");
        site.Repository.Write(OtherListKey, OptionListKind.Manual, items: """[{"value":"b"}]""");
        var service = site.Options();
        async Task<int> ReadsAfterBothAsync()
        {
            await service.GetListAsync(ListKey, new FormBuilderOptionContext("en"));
            await service.GetListAsync(OtherListKey, new FormBuilderOptionContext("en"));
            return site.Repository.Reads;
        }

        var first = await ReadsAfterBothAsync();
        var kept = await ReadsAfterBothAsync();
        site.Refresher.Refresh([new OptionListCacheChange(ListKey)]);
        var one = await ReadsAfterBothAsync();
        site.Refresher.Refresh([new OptionListCacheChange(Guid.Empty)]);
        var unreadable = await ReadsAfterBothAsync();
        site.Refresher.RefreshAll();
        var all = await ReadsAfterBothAsync();

        Assert.That(new[] { first, kept, one, unreadable, all }, Is.EqualTo(new[] { 2, 2, 3, 5, 7 }));
    }

    [Test]
    public void TheRefresher_HasItsFixedIdAndName()
    {
        var refresher = new OptionListTestSite().Refresher;

        Assert.That((refresher.RefresherUniqueId, refresher.Name), Is.EqualTo((Guid.Parse("8ca74819-3a1e-4d49-b9fe-9de5d8347f07"), "Jiron Form Builder option lists")));
    }

    private static bool SameRequest(FormBuilderOptionRequest a, FormBuilderOptionRequest b) =>
        a.Culture == b.Culture && a.Page == b.Page && a.ParentValues.SequenceEqual(b.ParentValues);
}
