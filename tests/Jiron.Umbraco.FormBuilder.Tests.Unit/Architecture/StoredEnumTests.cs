using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// The repositories store their enums only through the one converter (ADR-0033; this check 4.8g): no code of Core's
/// persistence apart from <see cref="StoredNames"/> and its table of names parses an enum from text, turns one into
/// text, boxes one (as a statement's parameter or a message's argument would) or names one of its values with
/// <c>nameof</c>. StoredNamesTests shows the converter is right; this shows nothing goes around it. The scan reads the
/// compiled code, local functions and lambdas included (4.8i), so a value turned into text in an interpolated string,
/// or passed to a statement as a number, is found as well.
/// </summary>
[TestFixture]
public sealed partial class StoredEnumTests
{
    private const string Persistence = "Jiron.Umbraco.FormBuilder.Core.Persistence";

    private static readonly Assembly Core = typeof(FormBuilderCoreComposer).Assembly;

    private static readonly string[] AroundTheConverter = ["Parse", "TryParse", "GetName", "GetNames", "ToString", "Format"];

    // What the compiler writes for every record: its text, its equality and its copy.
    private static readonly string[] RecordMembers = ["ToString", "PrintMembers", "GetHashCode", "Equals", "<Clone>$", "get_EqualityContract", "op_Equality", "op_Inequality", "Deconstruct"];

    [Test]
    public void ThePersistenceCode_StoresEnums_OnlyThroughTheConverter()
    {
        var found = PersistenceTypes().SelectMany(Methods)
            .SelectMany(method => Uses(method).Select(use => $"{method.DeclaringType!.FullName}.{method.Name}: {use}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.That(found, Is.Empty);
    }

    [Test]
    public void ThePersistenceSource_NamesNoEnumValue_WithNameof()
    {
        var enums = Core.GetTypes().Where(type => type.IsEnum).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var folder = RepositoryRoot.Path(typeof(StoredEnumTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Core", "Persistence");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != "StoredNames.cs").ToList();
        var found = files
            .SelectMany(path => Nameof().Matches(File.ReadAllText(path))
                .Where(match => enums.Contains(match.Groups["type"].Value))
                .Select(match => $"{Path.GetFileName(path)}: {match.Value}"))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(files.Select(Path.GetFileName), Has.Member("EntryRepository.cs").And.Member("EntryQuerySql.cs"), "the scan read the persistence code");
            Assert.That(found, Is.Empty);
        });
    }

    [Test]
    public void TheScan_ReadsThePersistenceCode_AndFindsEveryWayAroundTheConverter()
    {
        var uses = Methods(typeof(Samples)).ToDictionary(method => method.Name, method => Uses(method).ToList());
        var converter = Core.GetTypes().Where(IsConverter).SelectMany(Methods).SelectMany(Uses).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(PersistenceTypes().SelectMany(Methods).Count(), Is.GreaterThan(500), "the methods read");
            Assert.That(converter, Has.Member("turns T into text"), "the converter's table of names turns them into text");
            Assert.That(uses[nameof(Samples.Parsed)], Is.EqualTo(new[] { "Parse of an enum" }));
            Assert.That(uses[nameof(Samples.TriedToParse)], Is.EqualTo(new[] { "TryParse of an enum" }));
            Assert.That(uses[nameof(Samples.Named)], Is.EqualTo(new[] { "GetName of an enum" }));
            Assert.That(uses[nameof(Samples.Written)], Is.EqualTo(new[] { "turns DayOfWeek into text" }));
            Assert.That(uses[nameof(Samples.Interpolated)], Is.EqualTo(new[] { "writes DayOfWeek into a text" }));
            Assert.That(uses[nameof(Samples.Boxed)], Is.EqualTo(new[] { "boxes DayOfWeek" }));
            Assert.That(uses[nameof(Samples.Checked)], Is.Empty, "a check that the value is defined");
            Assert.That(uses[nameof(Samples.Counted)], Is.Empty, "a flags value stored as its number");
            Assert.That(uses.Single(pair => pair.Key.Contains("g__Text", StringComparison.Ordinal)).Value, Is.EqualTo(new[] { "turns DayOfWeek into text" }), "a local function");
            Assert.That(Methods(typeof(Row)).SelectMany(Uses), Is.Empty, "a record's own text, left out");
            Assert.That(IlReader.Methods(typeof(Row)).SelectMany(Uses), Is.Not.Empty, "which turns its values into text");
        });
    }

    [TestCase("nameof(EntryState.Approved)", true)]
    [TestCase("nameof(entry.State)", false)]
    [TestCase("nameof(StoredEntry)", false)]
    public void TheSourceScan_FindsAValueNamedWithNameof(string code, bool found) =>
        Assert.That(Nameof().IsMatch(code), Is.EqualTo(found));

    private static IEnumerable<Type> PersistenceTypes() =>
        Core.GetTypes().Where(type => type.Namespace is { } name && (name == Persistence || name.StartsWith(Persistence + ".", StringComparison.Ordinal)) && !IsConverter(type));

    private static bool IsConverter(Type type) =>
        type == typeof(StoredNames) || type == typeof(EnumNames<>) || (type.DeclaringType is { } outer && IsConverter(outer));

    // What the compiler writes for a record is left out: no row is written with it. What it writes for local functions
    // and lambdas is read, as code of the method they are in.
    private static IEnumerable<MethodBase> Methods(Type type) =>
        IlReader.Methods(type).Where(method => !(method.IsDefined(typeof(CompilerGeneratedAttribute)) && RecordMembers.Contains(method.Name)));

    // An enum, or a type parameter that only enums fill.
    private static bool IsEnum(Type type) => type.IsEnum || (type.IsGenericParameter && type.GetGenericParameterConstraints().Contains(typeof(Enum)));

    // What a method does with an enum around the converter: a parse, a value turned into text, or a value boxed.
    private static IEnumerable<string> Uses(MethodBase method)
    {
        var (typeArguments, methodArguments) = IlReader.Context(method);
        Type? constrained = null;
        foreach (var (code, _, token) in IlReader.Read(method))
        {
            if (code == OpCodes.Constrained)
            {
                constrained = method.Module.ResolveType(token, typeArguments, methodArguments);
            }
            else if (code == OpCodes.Box && method.Module.ResolveType(token, typeArguments, methodArguments) is { } boxed && IsEnum(boxed))
            {
                yield return $"boxes {boxed.Name}";
            }
            else if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Ldftn)
            {
                var called = method.Module.ResolveMethod(token, typeArguments, methodArguments)!;
                if (called.DeclaringType == typeof(Enum) && AroundTheConverter.Contains(called.Name))
                {
                    yield return $"{called.Name} of an enum";
                }
                else if (called.Name == nameof(ToString) && constrained is not null && IsEnum(constrained))
                {
                    yield return $"turns {constrained.Name} into text";
                }
                else if (called is MethodInfo { IsGenericMethod: true, Name: "AppendFormatted" } formatted && formatted.GetGenericArguments().FirstOrDefault(IsEnum) is { } written)
                {
                    yield return $"writes {written.Name} into a text";
                }

                constrained = null;
            }
        }
    }

    // A value named in the source: nameof(Type.Member), where the type is one of Core's enums.
    [GeneratedRegex(@"nameof\((?<type>[A-Z][A-Za-z0-9]*)\.[A-Z][A-Za-z0-9]*\)")]
    private static partial Regex Nameof();

    // Each way around the converter, and two that are not, as the scan must tell them.
    private static class Samples
    {
        public static DayOfWeek Parsed(string text) => Enum.Parse<DayOfWeek>(text);

        public static bool TriedToParse(string text) => Enum.TryParse<DayOfWeek>(text, out _);

        public static string? Named(DayOfWeek day) => Enum.GetName(day);

        public static string Written(DayOfWeek day) => day.ToString();

        public static string Interpolated(DayOfWeek day) => $"on {day}";

        public static object Boxed(DayOfWeek day) => day;

        public static bool Checked(DayOfWeek day) => Enum.IsDefined(day);

        public static int Counted(AttributeTargets targets) => (int)targets;

        public static string InALocalFunction(DayOfWeek day)
        {
            return Text(day);

            static string Text(DayOfWeek value) => value.ToString();
        }
    }

    private sealed record Row(DayOfWeek Day);
}
