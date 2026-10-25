using System.Collections;
using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The lists of the definition model, its value types and the public projection (ADR-0034): no cast and no list a
/// record was built from changes them, and records compare them by their items, in order.
/// </summary>
public sealed class ModelListTests
{
    [Test]
    public void NoListOfAReadDefinition_OrOfItsProjection_CanBeChangedThroughACast()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var lists = Lists(definition).Concat(Lists(PublicProjection.Create(definition, FieldTypeCatalogue.BuiltIn))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lists.Select(list => list.Name).Distinct(), Is.SupersetOf(new[] { "FormDefinition.Pages", "FieldOption.ParentValues", "LocalizedText.Texts", "FieldDefaultValue.Values", "List.Items", "PublicForm.Pages", "PublicField.Fields" }), "the walk reaches the records', the value types' and the projection's lists");
            Assert.That(lists.Where(list => Changeable(list.List)).Select(list => $"{list.Name} is a {list.List.GetType().Name}").Distinct(), Is.Empty);
            Assert.Throws<InvalidCastException>(() => ((List<FormPage>)definition.Pages).Clear(), "a cast to a list");
            Assert.Throws<InvalidCastException>(() => ((ICollection<FormPage>)definition.Pages).Clear(), "a cast to a collection");
        });
    }

    [Test]
    public void AListARecordWasBuiltFrom_ChangesNothingInIt_Afterwards()
    {
        var pages = new List<FormPage> { new() { Key = Guid.NewGuid() } };
        var definition = new FormDefinition { Key = Guid.NewGuid(), Pages = pages };
        var values = new List<string> { "keynote" };
        var value = FieldDefaultValue.OfList(values);
        var operands = new List<ConditionOperand> { new ConditionOperand.Text("keynote") };
        var operand = new ConditionOperand.List(operands);
        var texts = new List<KeyValuePair<string, string>> { new(string.Empty, "Name") };
        var text = new LocalizedText(texts);

        pages.Add(new FormPage { Key = Guid.NewGuid() });
        values.Add("panel");
        operands.Add(new ConditionOperand.Text("panel"));
        texts.Add(new("de", "Name"));

        Assert.Multiple(() =>
        {
            Assert.That(definition.Pages, Has.Count.EqualTo(1), "the record");
            Assert.That(value.Values, Is.EqualTo(new[] { "keynote" }), "the default value");
            Assert.That(operand.Items, Has.Count.EqualTo(1), "the list operand");
            Assert.That(text.Texts, Has.Count.EqualTo(1), "the localised text");
        });
    }

    [Test]
    public void Records_CompareTheirLists_ByTheirItemsInOrder()
    {
        var first = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var second = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var page = second.Pages[^1];
        var group = page.Groups[^1];
        var deep = second with
        {
            Pages = [second.Pages[0], page with { Groups = [group with { Fields = [.. group.Fields.SkipLast(1), group.Fields[^1] with { Key = Guid.NewGuid() }] }] }],
        };
        var option = new FieldOption { Value = "keynote" };
        var other = new FieldOption { Value = "panel" };

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Equals(first), Is.True, "two reads of one document");
            Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()), "and their hash codes");
            Assert.That(deep.Equals(first), Is.False, "the last field of the last page changed");
            Assert.That((second with { Pages = [.. second.Pages.Reverse()] }).Equals(first), Is.False, "the same pages in another order");
            Assert.That(PublicProjection.Create(second, FieldTypeCatalogue.BuiltIn).Equals(PublicProjection.Create(first, FieldTypeCatalogue.BuiltIn)), Is.True, "two projections of equal definitions");
            Assert.That(new FieldOptions { Items = [option, other] }.Equals(new FieldOptions { Items = new List<FieldOption> { option, other } }), Is.True, "however the list was built");
            Assert.That(new FieldOptions().Equals(new FieldOptions { Items = new List<FieldOption>() }), Is.True, "an empty list and none");
        });
    }

    // Whether a cast to an array or to a collection interface could change the list.
    private static bool Changeable(object list) =>
        list is Array or IList || list.GetType().GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>));

    // Every list the object holds at any depth, named Type.Property: the records', the value types' and their items'.
    private static IEnumerable<(string Name, object List)> Lists(object root)
    {
        var pending = new Stack<object>([root]);
        while (pending.TryPop(out var current))
        {
            var type = current.GetType();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
            {
                var value = property.GetValue(current);
                if (value is null)
                {
                    continue;
                }

                if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                {
                    yield return ($"{type.Name}.{property.Name}", value);
                    foreach (var item in ((IEnumerable)value).Cast<object?>().Where(item => item is not null && IsModel(item.GetType())))
                    {
                        pending.Push(item!);
                    }
                }
                else if (IsModel(value.GetType()))
                {
                    pending.Push(value);
                }
            }
        }
    }

    private static bool IsModel(Type type) =>
        !type.IsEnum && type.Namespace?.StartsWith("Jiron.Umbraco.FormBuilder.Core.Definitions", StringComparison.Ordinal) == true;
}
