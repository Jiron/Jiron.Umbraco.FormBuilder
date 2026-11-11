using Jiron.Umbraco.FormBuilder.Core.Entries;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Samples.Notifications;

/// <summary>
/// The blocked domains sample's notification handler (docs/extending.md): before an entry is stored, it refuses an email
/// address at a domain the site blocks, such as a throwaway mail service, with an error at the field in the visitor's
/// language. The visitor keeps every answer and can give another address. Every email field of every form is checked;
/// a site's own handler may look at one form only.
/// </summary>
/// <param name="options">The domains the site blocks.</param>
public sealed class BlockedDomainsHandler(IOptionsMonitor<BlockedDomainsOptions> options) : INotificationAsyncHandler<EntrySubmittingNotification>
{
    /// <inheritdoc />
    public Task HandleAsync(EntrySubmittingNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var blocked = options.CurrentValue.Domains;
        if (blocked.Count == 0)
        {
            return Task.CompletedTask;
        }

        var fields = notification.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
            .Where(field => field.Type == "email" && field.Alias is not null);
        foreach (var field in fields)
        {
            var address = notification.AnswerOf(field.Alias!)?.Values.FirstOrDefault()?.Text;
            var at = address?.LastIndexOf('@') ?? -1;
            if (at > 0 && blocked.Contains(address![(at + 1)..].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                notification.AddError(field.Alias!, MessageIn(notification.Culture));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>The error in the visitor's language: German, or English for every other.</summary>
    /// <param name="culture">The visitor's culture, such as <c>de-CH</c>.</param>
    /// <returns>The error.</returns>
    public static string MessageIn(string culture) => culture.StartsWith("de", StringComparison.OrdinalIgnoreCase)
        ? "Bitte geben Sie eine Adresse an, die Sie dauerhaft nutzen."
        : "Please give an address you use for good.";
}

/// <summary>The domains the blocked domains sample refuses (<c>JironFormBuilderSamples:BlockedDomains</c>).</summary>
public sealed class BlockedDomainsOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "JironFormBuilderSamples:BlockedDomains";

    /// <summary>The domains, such as <c>throwaway.test</c>, compared without letter case.</summary>
    public IList<string> Domains { get; } = [];
}
