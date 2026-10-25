using System.Reflection;
using System.Runtime.CompilerServices;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Core keeps no static state that can change (the quality bar; ROADMAP 4.8f): every static field is read-only, and
/// none holds an array or a collection that adds, removes or replaces items. Its tables are frozen sets and
/// dictionaries, immutable arrays and read-only lists, the same on every server for as long as it runs.
/// </summary>
[TestFixture]
public sealed class StaticDataTests
{
    private static readonly Type[] Changeable =
    [
        typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(SortedSet<>), typeof(SortedDictionary<,>),
        typeof(SortedList<,>), typeof(Stack<>), typeof(Queue<>), typeof(LinkedList<>),
    ];

    [Test]
    public void EveryStaticFieldOfCore_IsReadOnly()
    {
        var writable = Fields().Where(field => !field.IsInitOnly).Select(Name).ToList();

        Assert.That(writable, Is.Empty, string.Join(Environment.NewLine, writable));
    }

    [Test]
    public void NoStaticFieldOfCore_HoldsAnArrayOrACollectionThatChanges()
    {
        // A field of an open generic type cannot be read, so its declared type has to do; any other is read, so a
        // read-only declaration with a changeable value behind it is found too.
        var changeable = Fields().Where(field => IsChangeable(IsOpen(field) ? field.FieldType : field.GetValue(null)?.GetType() ?? field.FieldType));

        var names = changeable.Select(Name).ToList();
        Assert.That(names, Is.Empty, string.Join(Environment.NewLine, names));
    }

    // Every static field Core declares itself, without constants and without what the compiler generates for lambdas.
    private static IEnumerable<FieldInfo> Fields() =>
        from type in typeof(FormDefinition).Assembly.GetTypes()
        where !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && !type.Name.StartsWith('<')
        from field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        where !field.IsLiteral
        select field;

    private static bool IsOpen(FieldInfo field) => field.DeclaringType!.ContainsGenericParameters || field.FieldType.ContainsGenericParameters;

    private static bool IsChangeable(Type type) =>
        type.IsArray || (type.IsGenericType && Changeable.Contains(type.GetGenericTypeDefinition()));

    private static string Name(FieldInfo field) => $"{field.DeclaringType!.FullName}.{field.Name}: {field.FieldType.Name}";
}
