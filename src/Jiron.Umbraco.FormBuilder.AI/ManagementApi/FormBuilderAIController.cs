using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.AI.ManagementApi;

/// <summary>
/// The base of the add-on's controllers (ADR-0018): Jiron Form Builder's management API controller, with its sign-in,
/// access declarations and refusals, in the add-on's own OpenAPI document <c>jfb-ai</c>, and an AI feature that cannot
/// work answered with its code.
/// </summary>
[MapToApi(FormBuilderAI.ApiName)]
[ServiceFilter(typeof(AIExceptionFilter))]
internal abstract class FormBuilderAIController : FormBuilderManagementApiController;

/// <summary>
/// Answers an AI feature that cannot work with its code: a missing profile <c>409</c>, a provider's failure <c>502</c>, a
/// reply the add-on cannot use <c>422</c>, a language that is not one to translate into <c>400</c>, a field not in the
/// draft <c>404</c>. The answer never quotes a prompt, a reply or the provider's message.
/// </summary>
internal sealed class AIExceptionFilter : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Exception is AIUnavailableException unavailable)
        {
            context.Result = Answer(unavailable.Code);
            context.ExceptionHandled = true;
        }
    }

    /// <summary>The answer to a code of <see cref="AIProblems"/>.</summary>
    public static FormBuilderProblemResult Answer(string code) => code switch
    {
        AIProblems.NoProfile => FormBuilderProblems.Problem(StatusCodes.Status409Conflict, code, "The AI add-on has no profile to use.", "Choose one in Settings > AI, or set a default chat profile in Umbraco.AI."),
        AIProblems.ProfileMissing => FormBuilderProblems.Problem(StatusCodes.Status409Conflict, code, "The AI profile chosen in Settings > AI no longer exists."),
        AIProblems.ProfileCannotChat => FormBuilderProblems.Problem(StatusCodes.Status409Conflict, code, "The AI profile chosen in Settings > AI cannot chat."),
        AIProblems.ProviderFailed => FormBuilderProblems.Problem(StatusCodes.Status502BadGateway, code, "The AI provider could not answer. Try again in a moment."),
        AIProblems.LanguageInvalid => FormBuilderProblems.Problem(StatusCodes.Status400BadRequest, code, "The language is not one of the site's other languages."),
        AIProblems.FieldMissing => FormBuilderProblems.Problem(StatusCodes.Status404NotFound, code, "The field is not in the saved draft."),
        AIProblems.DraftInvalid => FormBuilderProblems.Problem(StatusCodes.Status422UnprocessableEntity, code, "The AI's draft is not a form Jiron Form Builder can use. Try again, or describe the form differently."),
        _ => FormBuilderProblems.Problem(StatusCodes.Status422UnprocessableEntity, AIProblems.ReplyUnusable, "The AI's answer could not be used. Try again."),
    };
}

/// <summary>
/// The document <c>jfb-ai</c>, which the add-on's backoffice client is made from (ADR-0018): on Umbraco 18, Umbraco's
/// document builder with the management API's settings, sign-in, operation names and schema rules (ADR-0250).
/// </summary>
internal static class FormBuilderAIDocument
{
    /// <summary>Registers the document once.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public static IUmbracoBuilder AddFormBuilderAIDocument(this IUmbracoBuilder builder) =>
        builder.AddFormBuilderBackOfficeDocument(
            FormBuilderAI.ApiName,
            "Jiron Form Builder AI add-on API",
            "The AI add-on's endpoints: drafting, translating and suggesting for backoffice users, each by its Jiron Form Builder permission.");
}
