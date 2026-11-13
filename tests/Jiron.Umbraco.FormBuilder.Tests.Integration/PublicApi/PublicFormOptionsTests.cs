using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The options endpoint of the public API (ADR-0086) on a real site with sources of its own: a field's options in the
/// culture, from its own items and from a source, for the answers a request gives up the field's chain of parents, each
/// held to the options its parent offers, so a text no option has never reaches a source (ADR-0081); <c>available</c>
/// false when a source fails; <c>no-cache</c> with an <c>ETag</c>; and the refusals of what it cannot answer.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormOptionsTests : PublicApiTestBase
{
    private static readonly FormField Country = Field("country", "dropdown") with
    {
        Options = new FieldOptions
        {
            Items =
            [
                new FieldOption
                {
                    Value = "ch",
                    Label = new LocalizedText([new(string.Empty, "Switzerland"), new("de", "Schweiz")]),
                    Description = new LocalizedText([new(string.Empty, "Four languages"), new("de", "Vier Sprachen")]),
                    Image = Guid.Parse("40000000-0000-4000-8000-000000000001"),
                },
                new FieldOption { Value = "de", Label = new LocalizedText([new(string.Empty, "Germany"), new("de", "Deutschland")]) },
                new FieldOption { Value = "at" },
            ],
        },
    };

    private static readonly FormField Region = Field("region", "dropdown") with
    {
        Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"regions"}"""), Parent = Country.Key },
    };

    private static readonly FormField City = Field("city", "dropdown") with
    {
        Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"cities"}"""), Parent = Region.Key },
    };

    // Its own items, one of them for Switzerland alone.
    private static readonly FormField Plug = Field("plug", "radio") with
    {
        Options = new FieldOptions
        {
            Items = [new FieldOption { Value = "j", Label = new LocalizedText("Type J"), ParentValues = ["ch"] }, new FieldOption { Value = "c", Label = new LocalizedText("Type C") }],
            Parent = Country.Key,
        },
    };

    private static readonly FormField Broken = Field("broken", "dropdown") with
    {
        Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"broken"}""") },
    };

    private static readonly FormField BelowBroken = Field("belowBroken", "dropdown") with
    {
        Options = new FieldOptions { Items = [new FieldOption { Value = "x", Label = new LocalizedText("X") }], Parent = Broken.Key },
    };

    private static readonly FormField Meal = Field("meal", "dropdown") with
    {
        Options = new FieldOptions { Items = [new FieldOption { Value = "veg", Label = new LocalizedText("Vegetarian") }, new FieldOption { Value = "fish", Label = new LocalizedText("Fish") }] },
    };

    private static readonly FormField Guests = Field("guests", "repeater") with { Fields = [Meal] };

    private static readonly FormField[] All = [NameField, Country, Region, City, Plug, Broken, BelowBroken, Guests];

    private SourceCalls Calls => GetRequiredService<SourceCalls>();

    [Test]
    public async Task AFieldsOptions_ComeInTheCulture_FromItsOwnItems_AndFromItsSource_ForThePage_WithATagAndNoCache()
    {
        await GermanAsync();
        var page = await PageAsync();
        var key = await PublishAsync(fields: All);

        using var countries = await GetAsync($"{Root}/forms/{key}/options/{Country.Key}?culture=DE-de");
        using var regions = await GetAsync($"{Root}/forms/{key}/options/{Region.Key}?parent=ch&contentKey={page}");
        var regionCalls = Calls.Taken();
        using var noPage = await GetAsync($"{Root}/forms/{key}/options/{Region.Key}?parent=ch&contentKey={Guid.NewGuid()}");
        var noPageCalls = Calls.Taken();
        using var meals = await GetAsync($"{Root}/forms/{key}/options/{Meal.Key}?culture=fr-FR");
        var country = await JsonAsync(countries);

        Assert.Multiple(async () =>
        {
            Assert.That(countries.StatusCode, Is.EqualTo(HttpStatusCode.OK), await countries.Content.ReadAsStringAsync());
            Assert.That(countries.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(Header(countries, "Cache-Control"), Is.EqualTo("no-cache"));
            Assert.That(countries.Headers.ETag?.Tag, Does.Match("^\"[0-9a-f]{32}\"$"));
            Assert.That(country.AsObject().Select(property => property.Key), Is.EqualTo(new[] { "culture", "available", "options" }));
            Assert.That((country["culture"]!.GetValue<string>(), country["available"]!.GetValue<bool>()), Is.EqualTo(("de-DE", true)), "the site's language, as Umbraco spells it");
            Assert.That(country["options"]!.AsArray().Select(option => option!.ToJsonString()), Is.EqualTo(new[]
            {
                """{"value":"ch","label":"Schweiz","description":"Vier Sprachen","image":"40000000-0000-4000-8000-000000000001"}""",
                """{"value":"de","label":"Deutschland"}""",
                """{"value":"at","label":"at"}""",
            }), "the labels in the culture, a missing one as the value, a description and an image only where an option has them");
            Assert.That(Options(await JsonAsync(regions)), Is.EqualTo(new[] { "zh: Zurich", "be: Bern", "other: Elsewhere" }));
            Assert.That(regionCalls, Is.EqualTo(new[] { $"regions: ch, en-US, page {page}" }), "the culture held to the default, the page as the request names it");
            Assert.That(noPage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(noPageCalls, Is.EqualTo(new[] { "regions: ch, en-US, no page" }), "content that is not published is no page");
            Assert.That(Options(await JsonAsync(meals)), Is.EqualTo(new[] { "veg: Vegetarian", "fish: Fish" }), "a repeatable group's field");
            Assert.That((await JsonAsync(meals))["culture"]!.GetValue<string>(), Is.EqualTo("en-US"), "a culture the site lacks is answered in the default language");
        });
    }

    [Test]
    public async Task AParentAnswerTheParentLacks_GivesTheOptionsOfNoAnswer_AndTheSourceNeverSeesIt()
    {
        var key = await PublishAsync(fields: All);

        using var none = await GetAsync($"{Root}/forms/{key}/options/{Region.Key}");
        using var lacked = await GetAsync($"{Root}/forms/{key}/options/{Region.Key}?parent=%3Cscript%3E&parent=fr");
        using var mixed = await GetAsync($"{Root}/forms/{key}/options/{Region.Key}?parent=fr&parent=%20ch&parent=ch");
        using var plugsLacked = await GetAsync($"{Root}/forms/{key}/options/{Plug.Key}?parent=fr");
        using var plugs = await GetAsync($"{Root}/forms/{key}/options/{Plug.Key}?parent=ch");
        var calls = Calls.Taken();

        Assert.Multiple(async () =>
        {
            Assert.That(Options(await JsonAsync(none)), Is.EqualTo(new[] { "other: Elsewhere" }), "the options of every answer");
            Assert.That(Options(await JsonAsync(lacked)), Is.EqualTo(new[] { "other: Elsewhere" }));
            Assert.That(lacked.Headers.ETag, Is.EqualTo(none.Headers.ETag), "the same answer as for no answer");
            Assert.That(Options(await JsonAsync(mixed)), Is.EqualTo(new[] { "zh: Zurich", "be: Bern", "other: Elsewhere" }), "an answer cleaned as answers are, once");
            Assert.That(Options(await JsonAsync(plugsLacked)), Is.EqualTo(new[] { "c: Type C" }), "a field's own items held alike");
            Assert.That(Options(await JsonAsync(plugs)), Is.EqualTo(new[] { "j: Type J", "c: Type C" }));
            Assert.That(calls, Is.EqualTo(new[] { "regions: (none), en-US, no page", "regions: (none), en-US, no page", "regions: ch, en-US, no page" }), "no text the country lacks reached the source");
        });
    }

    [Test]
    public async Task AThreeLevelChain_HoldsEachLevel_ToTheOptionsItsParentsHeldAnswersGive()
    {
        var key = await PublishAsync(fields: All);
        var results = new Dictionary<string, (string[] Options, string[] Calls)>();
        foreach (var (what, query) in new[]
        {
            ("the region of the country", $"parent=zh&parent.{Country.Key}=ch"),
            ("every level by its key", $"parent.{Region.Key}=zh&parent.{Country.Key:D}=ch"),
            ("the parent both ways", $"parent=zh&PARENT.{Region.Key.ToString("D").ToUpperInvariant()}=zh&parent.{Country.Key}=ch"),
            ("with a field outside the chain", $"parent=zh&parent.{Country.Key}=ch&parent.{NameField.Key}=x"),
            ("the region of another country", $"parent=zh&parent.{Country.Key}=de"),
            ("the region without a country", "parent=zh"),
        })
        {
            using var response = await GetAsync($"{Root}/forms/{key}/options/{City.Key}?{query}");
            results[what] = (Options(await JsonAsync(response)), Calls.Taken());
        }

        Assert.Multiple(() =>
        {
            Assert.That(results["the region of the country"].Options, Is.EqualTo(new[] { "zurich: Zurich" }));
            Assert.That(results["the region of the country"].Calls, Is.EqualTo(new[] { "regions: ch, en-US, no page", "cities: zh, en-US, no page" }));
            Assert.That(results["every level by its key"], Is.EqualTo(results["the region of the country"]), "the field's own parent by its key too");
            Assert.That(results["the parent both ways"], Is.EqualTo(results["the region of the country"]), "both names in any letter case, the answer once");
            Assert.That(results["with a field outside the chain"], Is.EqualTo(results["the region of the country"]), "an answer the chain does not name is not read");
            Assert.That(results["the region of another country"].Options, Is.Empty, "no city belongs to every region");
            Assert.That(results["the region of another country"].Calls, Is.EqualTo(new[] { "regions: de, en-US, no page", "cities: (none), en-US, no page" }), "a region its country does not offer is no answer");
            Assert.That(results["the region without a country"].Calls, Is.EqualTo(new[] { "regions: (none), en-US, no page", "cities: (none), en-US, no page" }));
        });
    }

    [Test]
    public async Task ASourceThatFails_IsUnavailable_AndSoIsAFieldItFilters()
    {
        var key = await PublishAsync(fields: All);

        using var broken = await GetAsync($"{Root}/forms/{key}/options/{Broken.Key}");
        using var below = await GetAsync($"{Root}/forms/{key}/options/{BelowBroken.Key}?parent=x");
        using var belowWithout = await GetAsync($"{Root}/forms/{key}/options/{BelowBroken.Key}");

        Assert.Multiple(async () =>
        {
            Assert.That(broken.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a page shows the field as one whose source failed");
            Assert.That((await JsonAsync(broken)).ToJsonString(), Is.EqualTo("""{"culture":"en-US","available":false,"options":[]}"""));
            Assert.That((await JsonAsync(below)).ToJsonString(), Is.EqualTo("""{"culture":"en-US","available":false,"options":[]}"""), "a parent whose options are unknown holds no answer");
            Assert.That(Options(await JsonAsync(belowWithout)), Is.EqualTo(new[] { "x: X" }), "without an answer to hold, its own options");
        });
    }

    [Test]
    public async Task TheETag_FollowsTheAnswer_AndAClientThatHoldsItGetsNotModified()
    {
        var key = await PublishAsync(fields: All);
        var path = $"{Root}/forms/{key}/options/{Plug.Key}?parent=ch";
        using var first = await GetAsync(path);
        var etag = first.Headers.ETag!;

        using var held = await GetAsync(path, request => request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag, isWeak: true)));
        using var other = await GetAsync($"{Root}/forms/{key}/options/{Plug.Key}?parent=de", request => request.Headers.IfNoneMatch.Add(etag));

        Assert.Multiple(async () =>
        {
            Assert.That(held.StatusCode, Is.EqualTo(HttpStatusCode.NotModified), "If-None-Match compares weakly");
            Assert.That(held.Headers.ETag, Is.EqualTo(etag));
            Assert.That(Header(held, "Cache-Control"), Is.EqualTo("no-cache"));
            Assert.That(await held.Content.ReadAsByteArrayAsync(), Is.Empty);
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK), "other answers, another tag");
        });
    }

    [Test]
    public async Task WhatItCannotAnswer_IsRefused_AFormAsTheFormEndpointRefusesIt()
    {
        var key = await PublishAsync(fields: All);
        var notHeadless = await PublishAsync("Not headless", headless: false, fields: [Country]);
        var unpublished = (await Forms.CreateAsync(new Core.Forms.NewFormRequest("Draft only", UserKey: Editor))).Key;
        var trashed = await PublishAsync("Trashed", fields: [Country]);
        await Forms.TrashAsync(trashed, (await Forms.GetAsync(trashed))!.ConcurrencyVersion, Editor);

        var answers = new Dictionary<string, string>();
        foreach (var (what, path) in new[]
        {
            ("a key of no form", $"{Guid.NewGuid()}/options/{Country.Key}"),
            ("an alias", $"contactUs/options/{Country.Key}"),
            ("a key without hyphens", $"{key:N}/options/{Country.Key}"),
            ("an unpublished form", $"{unpublished}/options/{Country.Key}"),
            ("a form in the trash", $"{trashed}/options/{Country.Key}"),
            ("a key of no field", $"{key}/options/{Guid.NewGuid()}"),
            ("a field key without hyphens", $"{key}/options/{Country.Key:N}"),
            ("a field without options", $"{key}/options/{NameField.Key}"),
            ("a repeatable group", $"{key}/options/{Guests.Key}"),
            ("an unreadable culture", $"{key}/options/{Country.Key}?culture=not%20a%20culture"),
            ("an unreadable page", $"{key}/options/{Country.Key}?contentKey=home"),
            ("a parent name without a field key", $"{key}/options/{City.Key}?parent.region=zh"),
            ("a form not served headless", $"{notHeadless}/options/{Country.Key}"),
        })
        {
            using var response = await GetAsync($"{Root}/forms/{path}");
            answers[what] = response.StatusCode == HttpStatusCode.OK ? "200" : $"{await RefusalAsync(response)}, {Header(response, "Cache-Control")}";
        }

        Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
        {
            ["a key of no form"] = "404 form_not_found, no-store",
            ["an alias"] = "404 form_not_found, no-store",
            ["a key without hyphens"] = "404 form_not_found, no-store",
            ["an unpublished form"] = "404 form_not_found, no-store",
            ["a form in the trash"] = "404 form_not_found, no-store",
            ["a key of no field"] = "404 not_found, no-store",
            ["a field key without hyphens"] = "404 not_found, no-store",
            ["a field without options"] = "404 not_found, no-store",
            ["a repeatable group"] = "404 not_found, no-store",
            ["an unreadable culture"] = "400 invalid_request, no-store",
            ["an unreadable page"] = "400 invalid_request, no-store",
            ["a parent name without a field key"] = "400 invalid_request, no-store",
            ["a form not served headless"] = "200",
        }), "every published form answers, as the page that shows it does");
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<SourceCalls>();
        builder.JironFormBuilder().OptionSources().Append<RegionsOptionSource>().Append<CitiesOptionSource>().Append<BrokenOptionSource>();
    }

    private static FormField Field(string alias, string type) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
    };

    // Each option as "value: label".
    private static string[] Options(JsonNode answer) =>
        [.. answer["options"]!.AsArray().Select(option => $"{option!["value"]!.GetValue<string>()}: {option["label"]!.GetValue<string>()}")];

    private async Task GermanAsync()
    {
        var languages = GetRequiredService<ILanguageService>();
        var english = (await languages.GetDefaultLanguageAsync())!;
        await languages.CreateAsync(new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode }, Constants.Security.SuperUserKey);
    }

    // A published page "Shop" at the root.
    private async Task<Guid> PageAsync()
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        var contents = GetRequiredService<IContentService>();
        var shop = new Content("Shop", -1, type);
        contents.Save(shop);
        contents.Publish(shop, ["*"]);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IDocumentPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return shop.Key;
    }
}

/// <summary>What the test site's sources were asked for, in order, as "source: parent values, culture, page".</summary>
internal sealed class SourceCalls
{
    private readonly ConcurrentQueue<string> _calls = new();

    /// <summary>Notes a request.</summary>
    public void Add(string source, FormBuilderOptionRequest request) =>
        _calls.Enqueue($"{source}: {(request.ParentValues.Count == 0 ? "(none)" : string.Join("|", request.ParentValues))}, {request.Culture}, {(request.Page is { } page ? $"page {page}" : "no page")}");

    /// <summary>The requests noted since the last time, which it forgets.</summary>
    public string[] Taken()
    {
        var taken = new List<string>();
        while (_calls.TryDequeue(out var call))
        {
            taken.Add(call);
        }

        return [.. taken];
    }
}

/// <summary><c>regions</c>: each country's regions, and one of every country.</summary>
/// <param name="calls">Where it notes what it is asked for.</param>
internal sealed class RegionsOptionSource(SourceCalls calls) : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "regions";

    /// <inheritdoc />
    public string Label => "Regions";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        calls.Add(Name, request);
        return ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
        [
            new FormBuilderOption("zh", "Zurich") { ParentValues = ["ch"] },
            new FormBuilderOption("be", "Bern") { ParentValues = ["ch"] },
            new FormBuilderOption("by", "Bavaria") { ParentValues = ["de"] },
            new FormBuilderOption("other", "Elsewhere"),
        ]);
    }
}

/// <summary><c>cities</c>: each region's cities, none of every region.</summary>
/// <param name="calls">Where it notes what it is asked for.</param>
internal sealed class CitiesOptionSource(SourceCalls calls) : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "cities";

    /// <inheritdoc />
    public string Label => "Cities";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        calls.Add(Name, request);
        return ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
        [
            new FormBuilderOption("zurich", "Zurich") { ParentValues = ["zh"] },
            new FormBuilderOption("munich", "Munich") { ParentValues = ["by"] },
        ]);
    }
}

/// <summary><c>broken</c>: a source that fails.</summary>
internal sealed class BrokenOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "broken";

    /// <inheritdoc />
    public string Label => "Broken";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The catalogue is down.");
}
