using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// An email action's live preview (ROADMAP 10.2b, ADR-0189): the mail of the draft the action's sidebar holds, as the team
/// gets it for the sample entry, rendered as a run renders it with the site's sender and layout, and everyone it goes to.
/// Nothing is queued. It needs <c>design</c> on the form, as the outcome's preview does, and it changes nothing.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/email-preview")]
[ApiExplorerSettings(GroupName = "Action type")]
internal sealed class EmailPreviewController(
    EmailPreviews previews,
    FormDefinitionSerializer serializer,
    IFormBuilderFormService forms,
    ILanguageService languages) : FormBuilderManagementApiController
{
    /// <summary>The mail of one of the draft's email actions for the sample entry, in a language of the site.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The draft's definition, the action and the language.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EmailPreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Render(Guid key, EmailPreviewRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = serializer.Read(request.Definition.GetRawText());
        if (read.Definition is not { } definition)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var culture = (request.Culture is { Length: > 0 } asked ? await languages.GetAsync(asked) : null)?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var result = await previews.RenderAsync(form, definition, request.Action, culture, cancellationToken);
        return result.Status switch
        {
            EmailPreviewStatus.Done => Ok(EmailPreviewResponseModel.Of(result.Preview!, result.Direction)),
            EmailPreviewStatus.NotEmail => FormBuilderProblems.ActionNotEmail(),
            EmailPreviewStatus.ViewMissing => FormBuilderProblems.EmailViewMissing(),
            EmailPreviewStatus.SettingsInvalid => FormBuilderProblems.ActionSettingsInvalid(),
            _ => FormBuilderProblems.ActionNotFound(),
        };
    }
}

/// <summary>What an email action's preview is asked for.</summary>
internal sealed record EmailPreviewRequestModel
{
    /// <summary>The draft's definition as the action's sidebar would make it with Update, read as a draft save reads it.</summary>
    public required JsonElement Definition { get; init; }

    /// <summary>The key of the email action to preview, one of the definition's.</summary>
    public required Guid Action { get; init; }

    /// <summary>A language of the site, such as <c>de-DE</c>, the mail is written in; the site's default language when it is none of them.</summary>
    public string? Culture { get; init; }
}

/// <summary>An email action's mail as the team gets it for the sample entry (ADR-0189).</summary>
internal sealed record EmailPreviewResponseModel
{
    /// <summary>Who it comes from, with the name; empty when the site has no sender, which the health check names.</summary>
    public required string From { get; init; }

    /// <summary>The team's addresses it goes to, each once; empty when it has none yet.</summary>
    public required IReadOnlyList<string> To { get; init; }

    /// <summary>The address a mail of its own goes to, the sample's answer of the recipient field; null for none.</summary>
    public string? Visitor { get; init; }

    /// <summary>The addresses that get a copy.</summary>
    public required IReadOnlyList<string> Cc { get; init; }

    /// <summary>The addresses that get a blind copy.</summary>
    public required IReadOnlyList<string> Bcc { get; init; }

    /// <summary>Where replies go, the sample's answer of the reply-to field; null for the sender.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>The subject, one line, with the sample's answers in place of its merge tags.</summary>
    public required string Subject { get; init; }

    /// <summary>The whole HTML document of the mail in its layout; null for a mail of plain text.</summary>
    public string? Html { get; init; }

    /// <summary>The text version, which a plain-text mail is.</summary>
    public required string Text { get; init; }

    /// <summary>Which way the language's text runs, <c>ltr</c> or <c>rtl</c>.</summary>
    public required string Direction { get; init; }

    /// <summary>The model of a preview.</summary>
    public static EmailPreviewResponseModel Of(EmailPreview preview, string direction)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var mail = preview.Mail;
        return new EmailPreviewResponseModel
        {
            From = mail.From,
            To = mail.To,
            Visitor = preview.Visitor,
            Cc = mail.Cc,
            Bcc = mail.Bcc,
            ReplyTo = mail.ReplyTo,
            Subject = mail.Subject,
            Html = mail.Html,
            Text = mail.Text,
            Direction = direction,
        };
    }
}
