using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Where answer recall may look (section 4.3.3, ADR-0068): a visitor text names only the fields before it, in the order
/// of the form, which is its pages, each page's groups, each group's fields and a repeatable group's children after it.
/// A field that holds the text, such as the repeatable group a child's label is in, is not before it. The browser's
/// runtime decides the same in <c>recall.ts</c>.
/// </summary>
internal static class MergeTagRecall
{
    /// <summary>
    /// Whether a field comes before a text, so that the text may recall its answer: always for a text after every field
    /// (<paramref name="textOwner"/> null, or a key the form lacks).
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="field">The key of the field the text names.</param>
    /// <param name="textOwner">The key of the page, group or field whose text it is.</param>
    public static bool IsBefore(FormDefinition form, Guid field, Guid? textOwner)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (textOwner is not { } owner)
        {
            return true;
        }

        var order = new Dictionary<Guid, int>();
        var holders = new Dictionary<Guid, Guid[]>();
        void Visit(Guid key, Guid[] held)
        {
            order.TryAdd(key, order.Count);
            holders[key] = held;
        }

        void Fields(IReadOnlyList<FormField> fields, Guid[] held)
        {
            foreach (var each in fields)
            {
                Visit(each.Key, held);
                Fields(each.Fields, [.. held, each.Key]);
            }
        }

        foreach (var page in form.Pages)
        {
            Visit(page.Key, []);
            foreach (var group in page.Groups)
            {
                Visit(group.Key, [page.Key]);
                Fields(group.Fields, [page.Key, group.Key]);
            }
        }

        if (!order.TryGetValue(owner, out var at))
        {
            return true;
        }

        return order.TryGetValue(field, out var position) && position < at && !holders[owner].Contains(field);
    }
}
