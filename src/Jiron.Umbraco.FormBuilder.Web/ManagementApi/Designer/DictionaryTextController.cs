using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// What a text that names a dictionary item (<c>#key</c>) shows in the designer (ADR-0228): the item's text in a language
/// of the site, found as visitors' texts find it. The canvas shows it in place of the key, and the box under the key says
/// it. Any user of the Forms section may ask: the texts are the site's public ones, which its pages show.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/dictionary-text")]
[ApiExplorerSettings(GroupName = "Dictionary")]
internal sealed class DictionaryTextController(MergeTagDictionary dictionary, ILanguageService languages) : FormBuilderManagementApiController
{
    /// <summary>The most keys one request asks for.</summary>
    public const int MostKeys = 100;

    /// <summary>The texts of dictionary items in a language of the site.</summary>
    /// <param name="key">The items' keys, dots included, without the <c>#</c>; at most <see cref="MostKeys"/>.</param>
    /// <param name="culture">A language of the site, such as <c>de-DE</c>; the site's default language when it is none of them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(DictionaryTextsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string[]? key = null, string? culture = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keys = (key ?? []).Where(one => !string.IsNullOrWhiteSpace(one)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length > MostKeys)
        {
            ModelState.AddModelError("key", $"Ask for at most {MostKeys} keys.");
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        if (Array.Find(keys, one => !LocalizedString.TryGetDictionaryKey("#" + one, out _)) is { } bad)
        {
            ModelState.AddModelError("key", $"'{bad}' is no dictionary key: letters, digits, dots, dashes and underscores.");
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        var language = (culture is null ? null : await languages.GetAsync(culture))?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var looked = await dictionary.LookUpAsync(keys, language);
        return Ok(new DictionaryTextsResponseModel
        {
            Culture = language,
            Texts = [.. keys.Select(one => new DictionaryTextResponseModel { Key = one, Found = looked[one] is not null, Text = looked[one] ?? string.Empty })],
        });
    }
}

/// <summary>Dictionary items' texts in one language (ADR-0228).</summary>
internal sealed record DictionaryTextsResponseModel
{
    /// <summary>The language the texts are in: the one asked for, or the site's default.</summary>
    public required string Culture { get; init; }

    /// <summary>Each key asked for, in the order asked.</summary>
    public required IReadOnlyList<DictionaryTextResponseModel> Texts { get; init; }
}

/// <summary>One dictionary item's text.</summary>
internal sealed record DictionaryTextResponseModel
{
    /// <summary>The key asked for.</summary>
    public required string Key { get; init; }

    /// <summary>Whether the site's dictionary has an item with this key.</summary>
    public required bool Found { get; init; }

    /// <summary>The item's text in the language or the first it falls back to; empty where none has one, or no item is found.</summary>
    public required string Text { get; init; }
}
