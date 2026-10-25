using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The save checks refuse what the writer cannot write before it runs (ROADMAP 4.8h, ADR-0034): a value outside its
/// enum and a required value that is missing, each at its path, for every such property of the model; depth alone
/// decides <c>limit_exceeded</c>; and the writer still refuses all of it, with the same message.
/// </summary>
public sealed class SaveCheckTests
{
    private const int Undefined = 99;

    private static readonly FormDefinitionSerializer Serializer = DefinitionFixtures.Serializer();

    [Test]
    public void EveryEnumProperty_WithAValueItsEnumDoesNotDefine_IsRefusedAtItsPath_AndByTheWriter()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var slot in Slots(property => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsEnum))
        {
            var (definition, owner, property, path) = slot.Open();
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Set(property, owner, Enum.ToObject(type, Undefined));
            covered.Add(slot.Name);

            var problems = Serializer.CheckSave(definition);
            var written = Refusal(definition);
            if (!Only(problems, DefinitionErrors.InvalidValue, path))
            {
                failures.Add($"{path} ({slot.Name}): {Describe(problems)}");
            }
            else if (written is null || !written.Contains(problems[0].Message, StringComparison.Ordinal) || !problems[0].Message.StartsWith($"{type.Name} has no value {Undefined};", StringComparison.Ordinal))
            {
                failures.Add($"{path} ({slot.Name}): the writer answered \"{written}\" to \"{problems[0].Message}\"");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
            Assert.That(covered, Is.SupersetOf(ModelSlots(property => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsEnum)), "the fixture reaches every enum property of the model");
        });
    }

    [Test]
    public void EveryRequiredValue_ThatIsMissing_IsRefusedAtItsPath_AndByTheWriter()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var slot in Slots(IsRequiredReference))
        {
            var (definition, owner, property, path) = slot.Open();
            Set(property, owner, null);
            covered.Add(slot.Name);

            var problems = Serializer.CheckSave(definition);
            if (!Only(problems, DefinitionErrors.InvalidValue, path) || problems[0].Message != "A required value is missing.")
            {
                failures.Add($"{path} ({slot.Name}): {Describe(problems)}");
            }
            else if (Refusal(definition) is null)
            {
                failures.Add($"{path} ({slot.Name}): the writer wrote it");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
            Assert.That(covered, Is.SupersetOf(ModelSlots(IsRequiredReference)), "the fixture reaches every required value of the model");
        });
    }

    [Test]
    public void AnUndefinedRuleEffect_IsRefusedAtItsPath_AndTheWriterRefusesItWithThatMessage()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var (path, rule) = Objects(definition).First(found => found.Value is FormRule);
        Set(Property(rule, "effect"), rule, (RuleEffect)Undefined);

        var problems = Serializer.CheckSave(definition);
        var thrown = Assert.Throws<ArgumentException>(() => FormDefinitionSerializer.Write(definition));

        Assert.Multiple(() =>
        {
            Assert.That(problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, path + ".effect") }));
            Assert.That(problems[0].Message, Does.StartWith("RuleEffect has no value 99; its words are show, hide,"));
            Assert.That(thrown!.Message, Does.Contain(problems[0].Message), "the writer says the same");
        });
    }

    [Test]
    public void ANullOperator_AndANullOptionValue_AreRefusedAtTheirPaths()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var objects = Objects(definition);
        var (conditionPath, condition) = objects.First(found => found.Value is RuleCondition);
        var (optionPath, option) = objects.First(found => found.Value is FieldOption);
        Set(Property(condition, "op"), condition, null);
        Set(Property(option, "value"), option, null);

        Assert.That(
            Serializer.CheckSave(definition).Select(problem => (problem.Code, problem.Path)),
            Is.EquivalentTo(new[] { (DefinitionErrors.InvalidValue, conditionPath + ".op"), (DefinitionErrors.InvalidValue, optionPath + ".value") }));
    }

    [Test]
    public void ARuleThatNestsTooDeep_IsRefusedAsTooDeep_AtTheRoot_AndByTheWriter()
    {
        // A hundred thousand groups: the checks stop at the depth no reader reads, so they cannot overflow the stack.
        var definition = WithNestedRule(100_000);

        var problems = Serializer.CheckSave(definition);
        var thrown = Assert.Throws<ArgumentException>(() => FormDefinitionSerializer.Write(definition));

        Assert.Multiple(() =>
        {
            Assert.That(problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, JsonPaths.Root) }));
            Assert.That(thrown!.Message, Does.StartWith(problems[0].Message), "the writer says the same");
        });
    }

    [Test]
    public void AtEveryDepth_WhatTheSaveChecksPass_Reads_AndTheRestIsTooDeep()
    {
        var passed = 0;
        var refused = 0;
        var failures = new List<string>();
        for (var groups = 0; groups <= 70; groups++)
        {
            var definition = WithNestedRule(groups);
            var problems = Serializer.CheckSave(definition);
            if (problems.Count == 0)
            {
                passed++;
                if (Serializer.Read(FormDefinitionSerializer.Write(definition)).Problems is { Count: > 0 } read)
                {
                    failures.Add($"{groups} groups: saved, but the reader answers {Describe(read)}");
                }
            }
            else
            {
                refused++;
                if (!Only(problems, DefinitionErrors.LimitExceeded, JsonPaths.Root))
                {
                    failures.Add($"{groups} groups: {Describe(problems)}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
            Assert.That((passed, refused), Is.Not.EqualTo((71, 0)).And.Not.EqualTo((0, 71)), "the walk crosses the limit");
        });
    }

    // The full fixture with one rule on its first page whose condition sits inside that many nested groups.
    private static FormDefinition WithNestedRule(int groups)
    {
        RuleItem item = new RuleCondition { Context = "culture", Operator = "equals", Value = new ConditionOperand.Text("de") };
        for (var i = 0; i < groups; i++)
        {
            item = new RuleGroup { Conditions = [item] };
        }

        var read = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var page = read.Pages[0] with { Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Show, Conditions = [item] }] };
        return read with { Pages = [page, .. read.Pages.Skip(1)] };
    }

    // Whether the problems are exactly one, of that code at that path.
    private static bool Only(IReadOnlyList<DefinitionProblem> problems, string code, string path) =>
        problems.Count == 1 && problems[0].Code == code && problems[0].Path == path;

    private static string Describe(IReadOnlyList<DefinitionProblem> problems) =>
        problems.Count == 0 ? "no problem" : string.Join("; ", problems.Select(problem => $"{problem.Code} at {problem.Path}: {problem.Message}"));

    // The writer's refusal, or null when it wrote the definition.
    private static string? Refusal(FormDefinition definition)
    {
        try
        {
            FormDefinitionSerializer.Write(definition);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    // A reference the model requires: not a list, which is empty rather than missing, and not one that may be null.
    private static bool IsRequiredReference(JsonPropertyInfo property) =>
        !property.PropertyType.IsValueType && !property.IsSetNullable && !IsList(property.PropertyType);

    private static bool IsList(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !ModelProperties.Leaves.Contains(type);

    // The model's properties are init-only, which the source-generated metadata does not set; reflection does.
    private static bool Writable(JsonPropertyInfo property) => Clr(property).SetMethod is not null;

    private static void Set(JsonPropertyInfo property, object owner, object? value) => Clr(property).SetValue(owner, value);

    private static PropertyInfo Clr(JsonPropertyInfo property) => (PropertyInfo)property.AttributeProvider!;

    private static JsonTypeInfo Info(Type type) => DefinitionJsonContext.Default.Options.GetTypeInfo(type);

    private static JsonPropertyInfo Property(object owner, string name) => Info(owner.GetType()).Properties.Single(property => property.Name == name);

    // Every property of the model the selector picks, as Type.property, read from the source-generated metadata.
    private static IEnumerable<string> ModelSlots(Func<JsonPropertyInfo, bool> picks) =>
        from type in ModelProperties.Clr
        where !type.Value.IsAbstract
        from property in Info(type.Value).Properties
        where Writable(property) && picks(property)
        select $"{type.Key}.{property.Name}";

    // Every place in the full fixture where the selector picks a property, each opened on a fresh read of it.
    private static IEnumerable<Slot> Slots(Func<JsonPropertyInfo, bool> picks)
    {
        var objects = Objects(DefinitionFixtures.Read(DefinitionFixtures.Full));
        for (var index = 0; index < objects.Count; index++)
        {
            var type = objects[index].Value.GetType();
            foreach (var property in Info(type).Properties.Where(property => Writable(property) && picks(property)))
            {
                yield return new Slot(index, property.Name, $"{type.Name}.{property.Name}");
            }
        }
    }

    // Every object of the model the definition holds, with its path, in the order a walk from the root finds them.
    private static List<(string Path, object Value)> Objects(FormDefinition definition)
    {
        var found = new List<(string Path, object Value)>();
        void Visit(string path, object value)
        {
            found.Add((path, value));
            foreach (var property in Info(value.GetType()).Properties)
            {
                var held = property.Get!(value);
                var at = JsonPaths.Property(path, property.Name);
                if (held is null || !IsModel(held.GetType()))
                {
                    continue;
                }

                if (IsList(held.GetType()))
                {
                    var i = 0;
                    foreach (var item in (IEnumerable)held)
                    {
                        if (item is not null && IsModel(item.GetType()))
                        {
                            Visit(JsonPaths.Index(at, i), item);
                        }

                        i++;
                    }
                }
                else
                {
                    Visit(at, held);
                }
            }
        }

        Visit(JsonPaths.Root, definition);
        return found;
    }

    // An object of the model with properties of its own; the leaves (texts, operands, settings) are values.
    private static bool IsModel(Type type) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DefinitionList<>)) ||
        (!type.IsEnum && !type.IsValueType && type.Namespace == typeof(FormDefinition).Namespace && !ModelProperties.Leaves.Any(leaf => leaf.IsAssignableFrom(type)));

    // One property of one object of the full fixture, found again on a fresh read so that each change starts clean.
    private sealed record Slot(int Index, string Property, string Name)
    {
        public (FormDefinition Definition, object Owner, JsonPropertyInfo Info, string Path) Open()
        {
            var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
            var (path, owner) = Objects(definition)[Index];
            return (definition, owner, SaveCheckTests.Property(owner, Property), JsonPaths.Property(path, Property));
        }
    }
}
