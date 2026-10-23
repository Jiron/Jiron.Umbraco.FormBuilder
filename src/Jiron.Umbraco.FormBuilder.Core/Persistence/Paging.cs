namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>Which rows of a list to read: the database pages, never the caller (ADR-0036).</summary>
internal readonly record struct PageRequest
{
    /// <summary>The most rows one page may hold.</summary>
    public const int MaxTake = 500;

    /// <summary>A page of <paramref name="take"/> rows after the first <paramref name="skip"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative skip, or a take outside 1 to <see cref="MaxTake"/>.</exception>
    public PageRequest(long skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxTake);
        Skip = skip;
        Take = take;
    }

    /// <summary>The rows to pass over.</summary>
    public long Skip { get; }

    /// <summary>The rows to read.</summary>
    public int Take { get; }

    /// <summary>The first page of <paramref name="take"/> rows.</summary>
    public static PageRequest First(int take) => new(0, take);
}

/// <summary>One page of a list, with the number of rows the whole list holds.</summary>
/// <typeparam name="T">The rows.</typeparam>
/// <param name="Items">The page's rows, in the list's order.</param>
/// <param name="Total">How many rows the list holds in all.</param>
internal sealed record Page<T>(IReadOnlyList<T> Items, long Total);
