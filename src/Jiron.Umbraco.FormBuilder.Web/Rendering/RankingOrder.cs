using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// A ranking's order as the page shows it (ADR-0096, ADR-0100): the options in the visitor's order, as the positions a
/// post without JavaScript gives its ranked options or as texts in order, then the others in the order the page showed
/// them, and any it did not show in their own order; and a move of one option by one place, which the post without
/// JavaScript makes, with the positions the page then posts.
/// </summary>
internal static class RankingOrder
{
    /// <summary>The options' values in the order the page shows them.</summary>
    /// <param name="options">The values of the options the page offers, in their own order.</param>
    /// <param name="answers">The ranking's answers: a position per ranked option's value, or the values in order.</param>
    /// <param name="shown">The options' values in the order the page showed them before, which places those not ranked; none for the options' own.</param>
    public static List<string> Of(IReadOnlyList<string> options, FieldAnswers answers, IReadOnlyList<string>? shown = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(answers);
        var given = answers.Parts.IsEmpty
            ? answers.Values.ToList()
            : [.. answers.Parts
                .Select(part => (Value: part.Key, Position: part.Value is [var text] && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var position) ? position : int.MaxValue))
                .OrderBy(part => part.Position)
                .ThenBy(part => part.Value, StringComparer.Ordinal)
                .Select(part => part.Value)];
        var offered = options.ToHashSet(StringComparer.Ordinal);
        var order = given.Where(offered.Contains).Distinct(StringComparer.Ordinal).ToList();
        var placed = order.ToHashSet(StringComparer.Ordinal);
        order.AddRange((shown ?? []).Where(offered.Contains).Where(placed.Add));
        order.AddRange(options.Where(placed.Add));
        return order;
    }

    /// <summary>The order with the option at a place moved up or down by one; the same order for a move past either end.</summary>
    /// <param name="order">The order shown.</param>
    /// <param name="place">The option's place, counted from 1, as the move button names it.</param>
    /// <param name="up">Whether it moves up, toward the first place.</param>
    public static List<string> Moved(IReadOnlyList<string> order, int place, bool up)
    {
        ArgumentNullException.ThrowIfNull(order);
        var at = place - 1;
        var to = up ? at - 1 : at + 1;
        var moved = order.ToList();
        if (at >= 0 && at < moved.Count && to >= 0 && to < moved.Count)
        {
            (moved[at], moved[to]) = (moved[to], moved[at]);
        }

        return moved;
    }

    /// <summary>The positions the page posts: one for each of the first options the ranking asks for, by its value.</summary>
    /// <param name="order">The order shown.</param>
    /// <param name="top">How many options the ranking asks for; null or 0 for all.</param>
    public static Dictionary<string, string> Positions(IReadOnlyList<string> order, int? top)
    {
        ArgumentNullException.ThrowIfNull(order);
        var ranked = top is > 0 ? Math.Min(top.Value, order.Count) : order.Count;
        return order.Take(ranked).Select((value, at) => (value, at)).ToDictionary(pair => pair.value, pair => (pair.at + 1).ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
    }
}
