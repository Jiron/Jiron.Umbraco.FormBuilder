using System.Text.Json;
using Jiron.Umbraco.FormBuilder.AI.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.Providers.Errors;

namespace Jiron.Umbraco.FormBuilder.AI.Chat;

/// <summary>One question of a feature: its alias for Umbraco.AI's audit and usage, the fixed instructions, the editor's or the form's text, and the shape of the answer.</summary>
/// <param name="Alias">The feature's alias, which Umbraco.AI's audit log and usage name (<c>jfb-draft-form</c>).</param>
/// <param name="Name">The feature's name there.</param>
/// <param name="Instructions">The fixed instructions, a system message.</param>
/// <param name="Content">What the feature asks about, inside its tagged block: the user message.</param>
/// <param name="Schema">The answer's JSON schema, which names itself in its <c>title</c>.</param>
/// <param name="MaxOutputTokens">The most the answer may spend.</param>
internal sealed record AIQuestion(string Alias, string Name, string Instructions, string Content, JsonElement Schema, int MaxOutputTokens);

/// <summary>
/// The add-on's one way to Umbraco.AI's chat (ADR-0018): the profile Settings > AI resolves, Umbraco.AI's inline chat with
/// the feature's alias and an output schema, and a failure turned into a refusal with a code. A provider's failure is
/// logged by its kind and Umbraco.AI's category only: never a prompt, a reply or the provider's message, which may quote
/// either.
/// </summary>
internal sealed partial class AIChat(IAIChatService chat, AIProfileChoice profiles, ILogger<AIChat> logger)
{
    /// <summary>Asks the question and answers the reply's text, never empty.</summary>
    /// <exception cref="AIUnavailableException">No profile, a provider's failure, or an empty reply.</exception>
    public async Task<string> AskAsync(AIQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        var profile = (await profiles.ResolveAsync(cancellationToken)).Usable();
        ChatResponse response;
        try
        {
            response = await chat.GetChatResponseAsync(
                builder => builder
                    .WithAlias(question.Alias)
                    .WithName(question.Name)
                    .WithProfile(profile.Id)
                    .WithOutputSchema(AIOutputSchema.FromJsonSchema(question.Schema))
                    // A fresh options object each time: Umbraco.AI merges into it (Tandem's research, section 4.4).
                    .WithChatOptions(new ChatOptions { MaxOutputTokens = question.MaxOutputTokens }),
                [new ChatMessage(ChatRole.System, question.Instructions), new ChatMessage(ChatRole.User, question.Content)],
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AIProviderException exception)
        {
            LogProviderFailed(logger, question.Alias, exception.Category.ToString());
            throw new AIUnavailableException(AIProblems.ProviderFailed, "The AI provider could not answer.", exception);
        }
        catch (Exception exception) when (exception is not AIUnavailableException)
        {
            LogFailed(logger, question.Alias, exception.GetType().Name);
            throw new AIUnavailableException(AIProblems.ProviderFailed, "The AI provider could not answer.", exception);
        }

        var text = response.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            LogEmpty(logger, question.Alias);
            throw new AIUnavailableException(AIProblems.ReplyUnusable, "The AI answered nothing.");
        }

        return text;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI provider failed the {Feature} request ({Category})")]
    private static partial void LogProviderFailed(ILogger logger, string feature, string category);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Feature} request to the AI failed ({Kind})")]
    private static partial void LogFailed(ILogger logger, string feature, string kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI answered the {Feature} request with nothing")]
    private static partial void LogEmpty(ILogger logger, string feature);
}
