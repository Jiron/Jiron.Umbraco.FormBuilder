using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Microsoft.Extensions.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.MergeTags;

/// <summary>
/// The switch of a site's composers that add merge-tag sources and filters (ADR-0065). They are found like any site's
/// composers, so they do nothing unless a test's site turns them on.
/// </summary>
internal static class SiteMergeTags
{
    /// <summary>The configuration key that turns the composers on.</summary>
    public const string Switch = "JironFormBuilderTests:SiteMergeTags";

    /// <summary>Whether the site turned them on.</summary>
    public static bool On(IUmbracoBuilder builder) => builder.Config.GetValue<bool>(Switch);
}

/// <summary>Adds a source and a filter, as a site's composer does, before the core composer runs.</summary>
[ComposeBefore(typeof(FormBuilderCoreComposer))]
public sealed class AddGreetingMergeTagsComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (SiteMergeTags.On(builder))
        {
            builder.JironFormBuilder().MergeTagSources().Append<GreetingMergeTagSource>();
            builder.JironFormBuilder().MergeTagFilters().Append<ShoutMergeTagFilter>();
        }
    }
}

/// <summary>Adds a second source, as a site's composer does, after the core composer ran.</summary>
[ComposeAfter(typeof(FormBuilderCoreComposer))]
public sealed class AddSeasonMergeTagsComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (SiteMergeTags.On(builder))
        {
            builder.JironFormBuilder().MergeTagSources().Append<SeasonMergeTagSource>();
        }
    }
}

/// <summary><c>greeting.text</c>: the site's greeting from its configuration, a singleton service the source takes.</summary>
/// <param name="configuration">The site's configuration.</param>
public sealed class GreetingMergeTagSource(IConfiguration configuration) : IFormBuilderMergeTagSource
{
    /// <summary>The configuration key that holds the greeting.</summary>
    public const string GreetingKey = "Acme:Greeting";

    /// <inheritdoc />
    public string Name => "greeting";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(Core.Definitions.FormDefinition form) => [new("greeting.text", "Greeting")];

    /// <inheritdoc />
    public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Path == "text" ? MergeTagValue.Of(configuration[GreetingKey] ?? string.Empty) : null);
}

/// <summary><c>season.name</c>: the same for everyone.</summary>
public sealed class SeasonMergeTagSource : IFormBuilderMergeTagSource
{
    /// <inheritdoc />
    public string Name => "season";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<MergeTagValue?>(MergeTagValue.Of("spring"));
}

/// <summary><c>| shout</c>: each item in capitals with an exclamation mark.</summary>
public sealed class ShoutMergeTagFilter : IFormBuilderMergeTagFilter
{
    /// <inheritdoc />
    public string Name => "shout";

    /// <inheritdoc />
    public bool Accepts(IReadOnlyList<MergeTagArgument> arguments) => arguments.Count == 0;

    /// <inheritdoc />
    public MergeTagValue Apply(MergeTagFilterRequest request) =>
        new([.. request.Value.Items.Select(item => new MergeTagItem(item.Text.ToUpperInvariant() + "!"))], request.Value.IsList);
}
