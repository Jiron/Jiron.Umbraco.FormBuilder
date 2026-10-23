using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Every object of the definition model with its JSON properties, read from the source-generated metadata: the one
/// description of the model that the reader, the schema test and the generator coverage test share.
/// </summary>
internal static class ModelProperties
{
    /// <summary>The leaf types: values with a converter of their own, which the schema describes as a whole.</summary>
    public static readonly IReadOnlySet<Type> Leaves = new HashSet<Type>
    {
        typeof(string), typeof(int), typeof(bool), typeof(Guid), typeof(DateTime), typeof(LocalizedText), typeof(LocalizedHtml),
        typeof(FieldDefaultValue), typeof(ConditionOperand), typeof(SettingsObject),
    };

    private static readonly Lazy<(Dictionary<string, Dictionary<string, string?>> Types, Dictionary<(string, string), string> Derived, Dictionary<string, Type> Clr)> Model = new(Build);

    /// <summary>Each object type's name with its properties, mapped to the object type each one holds (null for a leaf).</summary>
    public static IReadOnlyDictionary<string, Dictionary<string, string?>> Types => Model.Value.Types;

    /// <summary>The concrete type behind a polymorphic base and its <c>kind</c>.</summary>
    public static IReadOnlyDictionary<(string Base, string Kind), string> Derived => Model.Value.Derived;

    /// <summary>The .NET type of each object type's name.</summary>
    public static IReadOnlyDictionary<string, Type> Clr => Model.Value.Clr;

    /// <summary>Every property of every concrete object type, as <c>Type.property</c>.</summary>
    public static IEnumerable<string> All() =>
        from type in Types
        where type.Key is not ("RuleItem" or "FieldCheck")
        from property in type.Value.Keys
        select $"{type.Key}.{property}";

    /// <summary>The required properties of an object type.</summary>
    public static IEnumerable<string> Required(string type)
    {
        var info = DefinitionJsonContext.Default.Options.GetTypeInfo(Clr[type]);
        return info.Properties.Where(property => property.IsRequired || property.Name == "schemaVersion").Select(property => property.Name);
    }

    private static (Dictionary<string, Dictionary<string, string?>>, Dictionary<(string, string), string>, Dictionary<string, Type>) Build()
    {
        var types = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        var derived = new Dictionary<(string, string), string>();
        var clr = new Dictionary<string, Type>(StringComparer.Ordinal);
        var options = DefinitionJsonContext.Default.Options;

        string? Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (Leaves.Contains(type) || type.IsEnum)
            {
                return null;
            }

            if (type == typeof(RuleItem))
            {
                types.TryAdd("RuleItem", new Dictionary<string, string?>(StringComparer.Ordinal));
                Visit(typeof(RuleGroup));
                Visit(typeof(RuleCondition));
                return "RuleItem";
            }

            var info = options.GetTypeInfo(type);
            if (info.Kind == JsonTypeInfoKind.Enumerable)
            {
                return Visit(info.ElementType!);
            }

            if (info.Kind != JsonTypeInfoKind.Object)
            {
                throw new InvalidOperationException($"Unexpected model type {type}.");
            }

            var name = type.Name;
            if (!types.TryAdd(name, new Dictionary<string, string?>(StringComparer.Ordinal)))
            {
                return name;
            }

            clr[name] = type;
            if (info.PolymorphismOptions is { } polymorphism)
            {
                foreach (var subtype in polymorphism.DerivedTypes)
                {
                    var subtypeName = Visit(subtype.DerivedType)!;
                    types[subtypeName][polymorphism.TypeDiscriminatorPropertyName] = null;
                    derived[(name, (string)subtype.TypeDiscriminator!)] = subtypeName;
                }
            }

            foreach (var property in info.Properties)
            {
                types[name][property.Name] = Visit(property.PropertyType);
            }

            return name;
        }

        Visit(typeof(FormDefinition));
        return (types, derived, clr);
    }
}
