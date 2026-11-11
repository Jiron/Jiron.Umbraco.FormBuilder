using Jiron.Umbraco.FormBuilder.Web.ManagementApi;

namespace Jiron.Umbraco.FormBuilder.AI;

/// <summary>The AI add-on's names (ADR-0018).</summary>
internal static class FormBuilderAI
{
    /// <summary>The OpenAPI document of the add-on's endpoints, apart from the core's <c>jfb</c>.</summary>
    public const string ApiName = "jfb-ai";

    /// <summary>The route below <c>/umbraco</c>, before each controller's own part.</summary>
    public const string Route = FormBuilderManagementApi.Route + "/ai";

    /// <summary>The audit action of a form the add-on drafted, on the form.</summary>
    public const string DraftedAction = "form.drafted";

    /// <summary>The audit action of the profile chosen in Settings > AI.</summary>
    public const string ProfileChosenAction = "settings.aiProfileChosen";

    /// <summary>The key of the chosen profile's id in <c>jfbKeyValue</c>.</summary>
    public const string ProfileKey = "ai.profile";

    /// <summary>The section an audit entry of Settings > AI names, which the audit log places in the sidebar.</summary>
    public const string SettingsSection = "ai";

    /// <summary>The prefix of a drafted form's mark in <c>jfbKeyValue</c>: <c>ai.drafted.&lt;form key&gt;</c>.</summary>
    public const string MarkPrefix = "ai.drafted.";

    /// <summary>The comment of the version a draft is created as.</summary>
    public const string DraftedComment = "Drafted by AI, please review";
}

/// <summary>The codes of the add-on's refusals, as the management API answers them.</summary>
internal static class AIProblems
{
    /// <summary>No profile is chosen and Umbraco.AI has no default chat profile.</summary>
    public const string NoProfile = "ai_no_profile";

    /// <summary>The chosen profile no longer exists.</summary>
    public const string ProfileMissing = "ai_profile_missing";

    /// <summary>The chosen profile is not a chat profile.</summary>
    public const string ProfileCannotChat = "ai_profile_cannot_chat";

    /// <summary>The provider failed, timed out or refused the request.</summary>
    public const string ProviderFailed = "ai_provider_failed";

    /// <summary>The AI answered, but not with something the add-on can use.</summary>
    public const string ReplyUnusable = "ai_reply_unusable";

    /// <summary>The draft the AI wrote is not a form a save takes.</summary>
    public const string DraftInvalid = "ai_draft_invalid";

    /// <summary>The language is not one of the site's, or is its default.</summary>
    public const string LanguageInvalid = "ai_language_invalid";

    /// <summary>The field is not in the saved draft.</summary>
    public const string FieldMissing = "ai_field_missing";
}

/// <summary>An AI feature cannot work now, for the reason its code names.</summary>
/// <param name="code">One of <see cref="AIProblems"/>.</param>
/// <param name="message">What happened, for the log and the answer's detail; never a prompt or a reply.</param>
/// <param name="innerException">The provider's own failure, if any.</param>
internal sealed class AIUnavailableException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>One of <see cref="AIProblems"/>.</summary>
    public string Code { get; } = code;
}
