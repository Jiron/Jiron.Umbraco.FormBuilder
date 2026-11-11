using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.AI.Drafting;
using Jiron.Umbraco.FormBuilder.AI.Marks;
using Jiron.Umbraco.FormBuilder.AI.Profiles;
using Jiron.Umbraco.FormBuilder.AI.Suggesting;
using Jiron.Umbraco.FormBuilder.AI.Translating;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.AI.ManagementApi;

/// <summary>
/// The add-on's state and Settings > AI (ADR-0018): whether the features can work, which every user of the section may
/// ask so a screen can say why not, and the profile, which needs <c>manageSettings</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderAI.Route)]
[ApiExplorerSettings(GroupName = "AI")]
internal sealed class AIController(AIProfileChoice profiles) : FormBuilderAIController
{
    /// <summary>Whether the AI features can work, with the profile they use, or why they cannot.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("status")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(AIStatusResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default)
    {
        var state = await profiles.ResolveAsync(cancellationToken);
        return Ok(new AIStatusResponseModel
        {
            Ready = state.Problem is null && state.Profile is not null,
            Problem = state.Problem,
            ProfileName = state.Problem is null ? state.Profile?.Name : null,
        });
    }

    /// <summary>Settings > AI: the chosen profile, or null for Umbraco.AI's default chat profile.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("settings")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(AISettingsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken = default) =>
        Ok(new AISettingsResponseModel { ProfileId = await profiles.GetChosenAsync(cancellationToken) });

    /// <summary>Chooses the profile, a chat profile, or the default with null; audited.</summary>
    /// <param name="request">The choice.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("settings")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(AISettingsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveSettings(AISettingsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await profiles.ChooseAsync(request.ProfileId, RequestAccess.User.Key, cancellationToken);
        return Ok(new AISettingsResponseModel { ProfileId = await profiles.GetChosenAsync(cancellationToken) });
    }
}

/// <summary>Draft a form with AI (ADR-0018): <c>design</c> where the form lands, as for a blank form.</summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderAI.Route + "/draft")]
[ApiExplorerSettings(GroupName = "AI")]
internal sealed class AIDraftController(FormDrafter drafter) : FormBuilderAIController
{
    /// <summary>
    /// Drafts a form from a description or a pasted questionnaire and creates it as a draft, never published; answers
    /// <c>201</c> with the new form.
    /// </summary>
    /// <param name="request">The text and the folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "folderKey")]
    [ProducesResponseType(typeof(AIDraftResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create(AIDraftRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var drafted = await drafter.DraftAsync(new DraftRequest(request.Text, RequestAccess.Folder?.Key, RequestAccess.User.Key), cancellationToken);
        Response.Headers[Constants.Headers.GeneratedResource] = drafted.Form.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, new AIDraftResponseModel
        {
            Key = drafted.Form.Key,
            Name = drafted.Form.Name,
            DraftVersion = drafted.DraftVersion,
            Left = drafted.Left,
        });
    }
}

/// <summary>The add-on's help in the designer (ADR-0018): the mark, a translation and suggestions, each <c>design</c> on the form.</summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderAI.Route + "/form")]
[ApiExplorerSettings(GroupName = "AI")]
internal sealed class AIFormController(DraftMarks marks, FormTranslator translator, FieldSuggester suggester) : FormBuilderAIController
{
    /// <summary>The draft version the add-on drafted of the form, or null for a form it did not draft.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/mark")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(AIMarkResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Mark(Guid key, CancellationToken cancellationToken = default) =>
        Ok(new AIMarkResponseModel { DraftVersion = await marks.GetAsync(key, cancellationToken) });

    /// <summary>Translates the saved draft's texts into one of the site's other languages, for the designer to apply.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The language, and whether texts already in it are translated again.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/translation")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(AITranslationResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Translate(Guid key, AITranslationRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await translator.TranslateAsync(new TranslationRequest(key, request.Culture, request.ReplaceExisting), cancellationToken) is not { } translation)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        return Ok(new AITranslationResponseModel
        {
            Culture = translation.Culture,
            DraftVersion = translation.DraftVersion,
            Left = translation.Left,
            Texts = [.. translation.Texts.Select(text => new AITranslatedTextResponseModel { Path = text.Path, Source = text.Source, Text = text.Text, Html = text.Html })],
        });
    }

    /// <summary>Suggests a help text and improvements for one field of the saved draft, for the designer to apply.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The field.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/suggestions")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(AISuggestionsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Suggest(Guid key, AISuggestionRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await suggester.SuggestAsync(new SuggestionRequest(key, request.FieldKey), cancellationToken) is not { } suggestions)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        return Ok(new AISuggestionsResponseModel
        {
            DraftVersion = suggestions.DraftVersion,
            Help = suggestions.Help is { } help ? AISuggestedTextResponseModel.Of(help) : null,
            Improvements = [.. suggestions.Improvements.Select(AISuggestedTextResponseModel.Of)],
        });
    }
}

/// <summary>Whether the AI features can work.</summary>
internal sealed class AIStatusResponseModel
{
    /// <summary>True when they can.</summary>
    public required bool Ready { get; init; }

    /// <summary>Why they cannot: <c>ai_no_profile</c>, <c>ai_profile_missing</c> or <c>ai_profile_cannot_chat</c>; null when they can.</summary>
    public string? Problem { get; init; }

    /// <summary>The name of the profile they use, or null.</summary>
    public string? ProfileName { get; init; }
}

/// <summary>Settings > AI as it is.</summary>
internal sealed class AISettingsResponseModel
{
    /// <summary>The Umbraco.AI chat profile the add-on uses, or null for Umbraco.AI's default chat profile.</summary>
    public Guid? ProfileId { get; init; }
}

/// <summary>Settings > AI as the user chose it.</summary>
internal sealed class AISettingsRequestModel
{
    /// <summary>A chat profile of Umbraco.AI, or null for Umbraco.AI's default chat profile.</summary>
    public Guid? ProfileId { get; init; }
}

/// <summary>What a draft is made from.</summary>
internal sealed class AIDraftRequestModel
{
    /// <summary>A description of the form, or the text of a questionnaire.</summary>
    [Required]
    [StringLength(FormDrafter.MaxTextLength)]
    public required string Text { get; init; }

    /// <summary>The folder the form goes into, or null for the root.</summary>
    public Guid? FolderKey { get; init; }
}

/// <summary>A drafted form.</summary>
internal sealed class AIDraftResponseModel
{
    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The number of its draft version, which the mark names.</summary>
    public required int DraftVersion { get; init; }

    /// <summary>How many of the AI's fields were left out.</summary>
    public required int Left { get; init; }
}

/// <summary>The "Drafted by AI" mark of a form.</summary>
internal sealed class AIMarkResponseModel
{
    /// <summary>The draft version the add-on drafted, or null for a form it did not draft.</summary>
    public int? DraftVersion { get; init; }
}

/// <summary>What a translation is asked for.</summary>
internal sealed class AITranslationRequestModel
{
    /// <summary>One of the site's languages other than its default.</summary>
    [Required]
    [StringLength(85)]
    public required string Culture { get; init; }

    /// <summary>Whether texts already in the language are translated again.</summary>
    public bool ReplaceExisting { get; init; }
}

/// <summary>A translation of a form's texts.</summary>
internal sealed class AITranslationResponseModel
{
    /// <summary>The language, as the site names it.</summary>
    public required string Culture { get; init; }

    /// <summary>The draft version that was translated.</summary>
    public required int DraftVersion { get; init; }

    /// <summary>The texts and their translations, in the form's order; empty when every text is in the language already.</summary>
    public required IReadOnlyList<AITranslatedTextResponseModel> Texts { get; init; }

    /// <summary>How many texts came back without a usable translation.</summary>
    public required int Left { get; init; }
}

/// <summary>One text and its translation.</summary>
internal sealed class AITranslatedTextResponseModel
{
    /// <summary>The text's path in the definition.</summary>
    public required string Path { get; init; }

    /// <summary>The default text.</summary>
    public required string Source { get; init; }

    /// <summary>The translation.</summary>
    public required string Text { get; init; }

    /// <summary>Whether it is rich text.</summary>
    public required bool Html { get; init; }
}

/// <summary>What suggestions are asked for.</summary>
internal sealed class AISuggestionRequestModel
{
    /// <summary>The field.</summary>
    [Required]
    public required Guid FieldKey { get; init; }
}

/// <summary>Suggestions for a field.</summary>
internal sealed class AISuggestionsResponseModel
{
    /// <summary>The draft version they were made for.</summary>
    public required int DraftVersion { get; init; }

    /// <summary>A help text, or null.</summary>
    public AISuggestedTextResponseModel? Help { get; init; }

    /// <summary>At most three improvements to the field's own texts.</summary>
    public required IReadOnlyList<AISuggestedTextResponseModel> Improvements { get; init; }
}

/// <summary>One suggested text.</summary>
internal sealed class AISuggestedTextResponseModel
{
    /// <summary><c>label</c>, <c>help</c> or <c>placeholder</c>.</summary>
    public required string Setting { get; init; }

    /// <summary>The text's path in the definition.</summary>
    public required string Path { get; init; }

    /// <summary>The suggested default text.</summary>
    public required string Text { get; init; }

    /// <summary>Why, for an improvement; null for the help suggestion.</summary>
    public string? Reason { get; init; }

    /// <summary>The model of a suggestion.</summary>
    public static AISuggestedTextResponseModel Of(SuggestedText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new AISuggestedTextResponseModel { Setting = text.Setting, Path = text.Path, Text = text.Text, Reason = text.Reason };
    }
}
