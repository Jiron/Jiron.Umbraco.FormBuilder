using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// A captcha a form can choose (ADR-0012, ADR-0142), registered through <c>builder.JironFormBuilder().CaptchaProviders()</c>.
/// reCAPTCHA v2 and v3 and hCaptcha are built in, and the last registration of a name wins, so a site can replace one. Its
/// keys live in host configuration only. A provider without its keys is offered as not configured.
/// </summary>
public interface IFormBuilderCaptchaProvider
{
    /// <summary>The name a form's definition chooses it by, such as <c>hcaptcha</c>.</summary>
    string Name { get; }

    /// <summary>Whether it has its keys.</summary>
    bool IsConfigured { get; }

    /// <summary>What a page needs to show it; null without its keys. It never holds a secret.</summary>
    FormBuilderCaptchaClient? Client { get; }

    /// <summary>
    /// Whether a visitor's token passes. It never throws for what the visitor sent or what the provider answered: a provider
    /// that cannot answer is <see cref="FormBuilderCaptchaOutcome.Unavailable"/>.
    /// </summary>
    /// <param name="check">The token and what it is held to.</param>
    /// <param name="cancellationToken">Cancelled when the submission is.</param>
    Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken);
}

/// <summary>What a captcha's verification came to (ADR-0012).</summary>
public enum FormBuilderCaptchaOutcome
{
    /// <summary>The token passes.</summary>
    Passed,

    /// <summary>The token does not pass, and the visitor can try again.</summary>
    Failed,

    /// <summary>The provider could not say, so the entry goes to Spam: no lead is lost, and none runs actions unchecked.</summary>
    Unavailable,
}

/// <summary>What a page needs to show a captcha.</summary>
/// <param name="SiteKey">The provider's public key.</param>
/// <param name="ScriptAddress">The address of the provider's script, which a page loads only after consent (section 4.10).</param>
/// <param name="Action">The action a score-based provider's token is asked for; null for the others.</param>
public sealed record FormBuilderCaptchaClient(string SiteKey, string ScriptAddress, string? Action = null);

/// <summary>A token to verify.</summary>
/// <param name="Token">The token a visitor's page posted.</param>
/// <param name="Hosts">The site's own hosts the token may be made for, from its domains and allowed origins and never from the request; empty when it has none to compare.</param>
/// <param name="Settings">The form's settings for the provider, or null.</param>
public sealed record FormBuilderCaptchaCheck(string Token, IReadOnlySet<string> Hosts, SettingsObject? Settings);

/// <summary>The captcha providers a site has, in the order they are registered (ADR-0142).</summary>
public sealed class FormBuilderCaptchaProviderCollection : BuilderCollectionBase<IFormBuilderCaptchaProvider>
{
    /// <summary>A collection of the providers the builder creates.</summary>
    public FormBuilderCaptchaProviderCollection(Func<IEnumerable<IFormBuilderCaptchaProvider>> items)
        : base(items)
    {
    }

    /// <summary>The provider of a name, the last registered when there are several; null when the site has none.</summary>
    /// <param name="name">The name.</param>
    public IFormBuilderCaptchaProvider? Find(string? name) => this.LastOrDefault(provider => string.Equals(provider.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// The ordered collection of captcha providers (ADR-0142), reached through <c>builder.JironFormBuilder().CaptchaProviders()</c>.
/// It holds the built-in ones from the moment it is made.
/// </summary>
public sealed class FormBuilderCaptchaProviderCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderCaptchaProviderCollectionBuilder, FormBuilderCaptchaProviderCollection, IFormBuilderCaptchaProvider>
{
    /// <summary>A builder that holds the built-in providers.</summary>
    public FormBuilderCaptchaProviderCollectionBuilder() => Append([typeof(RecaptchaV2Provider), typeof(RecaptchaV3Provider), typeof(HcaptchaProvider)]);

    /// <inheritdoc />
    protected override FormBuilderCaptchaProviderCollectionBuilder This => this;
}
