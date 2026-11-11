using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The package's visitor texts a site or a form may reword (ADR-0230): each text's key, the group the editor lists it
/// under and the values in braces its wording may name, with the package's words in a language of the site, which the
/// editor shows as each box's placeholder. Any user of the Forms section may ask: the texts are the package's.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/visitor-texts")]
[ApiExplorerSettings(GroupName = "Visitor texts")]
internal sealed class VisitorTextsController(ILanguageService languages) : FormBuilderManagementApiController
{
    /// <summary>The visitor texts by group, with the package's words in a language.</summary>
    /// <param name="culture">A language of the site, such as <c>de-DE</c>; the site's default language when it is none of them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(VisitorTextsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string? culture = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var language = (culture is null ? null : await languages.GetAsync(culture))?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var words = PackageWords(language);
        return Ok(new VisitorTextsResponseModel
        {
            Culture = language,
            Groups = [.. VisitorTextKeys.Groups.Select(group => new VisitorTextGroupResponseModel
            {
                Alias = group,
                Texts = [.. VisitorTextKeys.All.Where(key => key.Group == group).Select(key => new VisitorTextResponseModel
                {
                    Key = key.Key,
                    Text = words.GetValueOrDefault(key.Key, string.Empty),
                    Values = [.. key.Values],
                })],
            })],
        });
    }

    /// <summary>The package's words of every visitor text in a culture, by key.</summary>
    /// <param name="culture">The culture.</param>
    internal static IReadOnlyDictionary<string, string> PackageWords(string culture)
    {
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in RenderingWords.Of(culture).ByName())
        {
            words[VisitorTextKeys.FormPrefix + name] = text;
        }

        foreach (var (name, text) in VisitorTextKeys.MessageTexts(VisitorMessageWords.Of(culture)))
        {
            words[VisitorTextKeys.MessagePrefix + name] = text;
        }

        words[VisitorTextKeys.AnswerPrefix + "yes"] = FieldDisplayWords.Yes(culture);
        words[VisitorTextKeys.AnswerPrefix + "no"] = FieldDisplayWords.No(culture);
        return words;
    }
}

/// <summary>The visitor texts a site or a form may reword, by group, in one language (ADR-0230).</summary>
internal sealed record VisitorTextsResponseModel
{
    /// <summary>The language the package's words are in: the one asked for, or the site's default.</summary>
    public required string Culture { get; init; }

    /// <summary>The groups, in the order the editor lists them.</summary>
    public required IReadOnlyList<VisitorTextGroupResponseModel> Groups { get; init; }
}

/// <summary>A group of visitor texts.</summary>
internal sealed record VisitorTextGroupResponseModel
{
    /// <summary>
    /// The group: <c>form</c> (around the questions), <c>files</c>, <c>signature</c>, <c>notices</c>, <c>after</c> (what
    /// follows a send), <c>errors</c> or <c>answers</c> (yes and no).
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>The group's texts, in the package's order.</summary>
    public required IReadOnlyList<VisitorTextResponseModel> Texts { get; init; }
}

/// <summary>One visitor text.</summary>
internal sealed record VisitorTextResponseModel
{
    /// <summary>Its key, such as <c>message.email</c>.</summary>
    public required string Key { get; init; }

    /// <summary>The package's words for it in the language.</summary>
    public required string Text { get; init; }

    /// <summary>The values in braces a wording may name, such as <c>{min}</c>.</summary>
    public required IReadOnlyList<string> Values { get; init; }
}
