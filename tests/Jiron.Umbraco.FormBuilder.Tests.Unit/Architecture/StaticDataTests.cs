using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Testing.Architecture;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Core and Web keep no static state that can change (the quality bar; ROADMAP 4.8f, 4.8h, 4.8i), by the rule that
/// <see cref="StaticDataRule"/> states, and the rule finds what can change and allows what cannot.
/// </summary>
[TestFixture]
public sealed class StaticDataTests
{
    private static readonly Assembly[] Assemblies = [typeof(FormDefinition).Assembly, typeof(FormBuilderProblems).Assembly];

    [Test]
    public void EveryStaticField_IsReadOnly()
    {
        var writable = StaticDataRule.Writable(Assemblies);

        Assert.That(writable, Is.Empty, string.Join(Environment.NewLine, writable));
    }

    [Test]
    public void EveryStaticField_HoldsOnlyWhatCannotChange()
    {
        var changeable = StaticDataRule.Changeable(Assemblies);

        Assert.That(changeable, Is.Empty, string.Join(Environment.NewLine, changeable));
    }

    [Test]
    public void TheCheck_FindsWhatCanChange_AndAllowsWhatCannot()
    {
        static string? Of(string name) => StaticDataRule.Why(typeof(Samples).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!);

        Assert.Multiple(() =>
        {
            Assert.That(Of(nameof(Samples.Concurrent)), Does.Contain("ConcurrentDictionary"), "a concurrent dictionary");
            Assert.That(Of(nameof(Samples.Options)), Does.Contain("not read-only"), "serializer options that can change");
            Assert.That(Of(nameof(Samples.Items)), Does.Contain("List"), "a list behind a read-only interface");
            Assert.That(Of(nameof(Samples.Numbers)), Does.Contain("array"), "an array");
            Assert.That(Of(nameof(Samples.Holder)), Does.Contain("Count"), "a writable field of an own type");
            Assert.That(Of(nameof(Samples.Deep)), Does.Contain("List"), "a list inside a frozen dictionary");
            Assert.That(Of(nameof(Samples.ReadOnlyOptions)), Is.Null, "read-only options");
            Assert.That(Of(nameof(Samples.Words)), Is.Null, "a frozen set");
            Assert.That(Of(nameof(Samples.Steps)), Is.Null, "an immutable array");
            Assert.That(Of(nameof(Samples.Fixed)), Is.Null, "an own record of read-only fields");
            Assert.That(Of(nameof(Samples.Later)), Does.Contain("List"), "a lazy list not made yet");
            Assert.That(Of(nameof(Samples.LaterFixed)), Is.Null, "a lazy own record not made yet");
            Assert.That(Of(nameof(Samples.Method)), Is.Null, "a reflection member");
            Assert.That(Samples.Later.IsValueCreated || Samples.LaterFixed.IsValueCreated, Is.False, "the check made no lazy value");
            Assert.That(StaticDataRule.Why(typeof(Holder<>).GetField(nameof(Holder<object>.Instance), BindingFlags.Static | BindingFlags.NonPublic)!), Does.Contain("type parameter"), "a field of an open type's parameter");
            Assert.That(StaticDataRule.Why(typeof(Holder<>).GetField(nameof(Holder<object>.Items), BindingFlags.Static | BindingFlags.NonPublic)!), Is.Null, "an immutable array of it");
        });
    }

    // What the check refuses and allows, as static fields of this assembly's own type.
    private static class Samples
    {
        internal static readonly ConcurrentDictionary<string, int> Concurrent = new();
        internal static readonly JsonSerializerOptions Options = new();
        internal static readonly IReadOnlyList<string> Items = new List<string> { "a" };
        internal static readonly int[] Numbers = [1, 2];
        internal static readonly Counter Holder = new();
        internal static readonly FrozenDictionary<string, IReadOnlyList<string>> Deep = new Dictionary<string, IReadOnlyList<string>> { ["a"] = new List<string>() }.ToFrozenDictionary();
        internal static readonly JsonSerializerOptions ReadOnlyOptions = ReadOnly(new JsonSerializerOptions());
        internal static readonly FrozenSet<string> Words = new[] { "a" }.ToFrozenSet();
        internal static readonly ImmutableArray<string> Steps = ["a"];
        internal static readonly Point Fixed = new(1, 2);
        internal static readonly Lazy<List<string>> Later = new(() => ["a"]);
        internal static readonly Lazy<Point> LaterFixed = new(() => new(1, 2));
        internal static readonly MethodInfo Method = typeof(Samples).GetMethod(nameof(ReadOnly), BindingFlags.Static | BindingFlags.NonPublic)!;

        private static JsonSerializerOptions ReadOnly(JsonSerializerOptions options)
        {
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }
    }

    // An open generic type's fields: one of its type parameter, which any class may fill, and an immutable array of it.
    private static class Holder<T>
        where T : class, new()
    {
        internal static readonly T Instance = new();
        internal static readonly ImmutableArray<T> Items = [];
    }

    private sealed class Counter
    {
        public int Count = 1;
    }

    private sealed record Point(int X, int Y);
}
