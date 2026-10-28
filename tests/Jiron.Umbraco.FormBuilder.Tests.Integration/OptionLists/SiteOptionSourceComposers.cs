using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Microsoft.Extensions.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;

/// <summary>
/// The switch of a site's composers that change the option sources (ADR-0073). They are found like any site's
/// composers, so they do nothing unless a test's site turns them on: <c>change</c> adds, replaces and removes sources,
/// <c>taken</c> adds one whose name a standard list has.
/// </summary>
internal static class SiteOptionSources
{
    /// <summary>The configuration key that turns the composers on.</summary>
    public const string Switch = "JironFormBuilderTests:SiteOptionSources";

    /// <summary>Whether the site turned these composers on.</summary>
    public static bool Is(IUmbracoBuilder builder, string value) => builder.Config.GetValue<string>(Switch) == value;
}

/// <summary>Adds the products and gives the months a site's own source, as a site's composer does, before the core composer runs.</summary>
[ComposeBefore(typeof(FormBuilderCoreComposer))]
public sealed class AddProductsOptionSourceComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (SiteOptionSources.Is(builder, "change"))
        {
            builder.JironFormBuilder().OptionSources().Append<ProductsOptionSource>().Replace<MonthsOptionSource, FiscalMonthsOptionSource>();
        }
    }
}

/// <summary>Removes "yes, no, not sure" and, when asked, adds a source whose name is taken, as a site's composer does, after the core composer ran.</summary>
[ComposeAfter(typeof(FormBuilderCoreComposer))]
public sealed class RemoveYesNoNotSureOptionSourceComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (SiteOptionSources.Is(builder, "change"))
        {
            builder.JironFormBuilder().OptionSources().Remove<YesNoNotSureOptionSource>();
        }

        if (SiteOptionSources.Is(builder, "taken"))
        {
            builder.JironFormBuilder().OptionSources().Append<ShopWeekdaysOptionSource>();
        }
    }
}

/// <summary><c>products</c>: the site's products, from its configuration, a singleton service the source takes.</summary>
/// <param name="configuration">The site's configuration.</param>
public sealed class ProductsOptionSource(IConfiguration configuration) : IFormBuilderOptionSource
{
    /// <summary>The configuration key that holds the products, separated by commas.</summary>
    public const string ProductsKey = "Acme:Products";

    /// <inheritdoc />
    public string Name => "products";

    /// <inheritdoc />
    public string Label => "Products";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([.. (configuration[ProductsKey] ?? string.Empty).Split(',').Select(product => new FormBuilderOption(product, product.ToUpperInvariant()))]);
}

/// <summary><c>months</c> in the site's own way: its fiscal year's, from April.</summary>
public sealed class FiscalMonthsOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "months";

    /// <inheritdoc />
    public string Label => "Fiscal months";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([new FormBuilderOption("4", "April"), new FormBuilderOption("5", "May")]);
}

/// <summary>A shop's opening days under the name of the standard weekdays, which start-up refuses.</summary>
public sealed class ShopWeekdaysOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "weekdays";

    /// <inheritdoc />
    public string Label => "Opening days";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([new FormBuilderOption("6", "Saturday")]);
}
