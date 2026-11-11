using System.Collections.Immutable;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Samples.Products;

/// <summary>
/// The products sample's option source (docs/extending.md): a choice field of the kind <c>code</c> names it
/// <c>sampleProducts</c> and offers the products the site's catalogue has in stock, each by its name in the visitor's
/// language with its price as its description, and each under its category, so a field whose parent is a choice of
/// categories offers only that category's products. The entry stores the product's number, which stays when a name
/// changes. The catalogue here is a fake (<see cref="FakeProductCatalogue"/>); a site's own calls its shop's API.
/// </summary>
/// <param name="catalogue">The site's catalogue.</param>
public sealed class ProductOptionSource(IProductCatalogue catalogue) : IFormBuilderOptionSource
{
    /// <summary>The name a field or a list names the source by.</summary>
    public const string SourceName = "sampleProducts";

    /// <inheritdoc />
    public string Name => SourceName;

    /// <inheritdoc />
    public string Label => "#jfbSamples_products";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var culture = CultureOf(request.Culture);
        var german = culture.TwoLetterISOLanguageName == "de";
        var products = await catalogue.ListAsync(cancellationToken);
        return
        [
            .. products
                .Where(product => product.InStock)
                .Select(product => new FormBuilderOption(product.Number, german ? product.GermanName : product.Name)
                {
                    Description = $"{product.Price.ToString("N2", culture)} EUR",
                    ParentValues = [product.Category],
                }),
        ];
    }

    private static CultureInfo CultureOf(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture.Length == 0 ? "en-US" : culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

/// <summary>A product of the site's catalogue.</summary>
/// <param name="Number">Its number, which the entry stores.</param>
/// <param name="Name">Its name in English.</param>
/// <param name="GermanName">Its name in German.</param>
/// <param name="Category">Its category, the value of the parent's choice it belongs to.</param>
/// <param name="Price">Its price in euros.</param>
/// <param name="InStock">Whether it can be ordered.</param>
public sealed record Product(string Number, string Name, string GermanName, string Category, decimal Price, bool InStock);

/// <summary>The site's product catalogue, as the option source reads it.</summary>
public interface IProductCatalogue
{
    /// <summary>The products, in the order the shop lists them.</summary>
    /// <param name="cancellationToken">Cancelled when the request ends; a source has 5 seconds.</param>
    /// <returns>The products.</returns>
    ValueTask<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The sample's catalogue, as a shop's API might answer: a fixed list of the library shop's products in three
/// categories, one of them out of stock.
/// </summary>
public sealed class FakeProductCatalogue : IProductCatalogue
{
    private static readonly ImmutableArray<Product> Listed =
    [
        new("B-101", "Library tote bag", "Bibliotheksbeutel", "gifts", 12.50m, true),
        new("B-102", "Reading lamp", "Leselampe", "gifts", 34.90m, true),
        new("B-103", "Bookmark set", "Lesezeichen-Set", "gifts", 4.20m, false),
        new("C-201", "Board game evening", "Spieleabend", "events", 8.00m, true),
        new("C-202", "Author reading", "Autorenlesung", "events", 15.00m, true),
        new("D-301", "Annual card", "Jahreskarte", "cards", 45.00m, true),
    ];

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<Product>>(Listed);
    }
}
