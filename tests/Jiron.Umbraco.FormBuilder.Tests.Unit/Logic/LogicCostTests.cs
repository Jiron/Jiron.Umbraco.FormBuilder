using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// What the logic costs on long answers (ROADMAP 4.8f): numbers compare digit by digit as the values they are, without
/// a number or a key being built, and a pattern's run keeps one set of buffers, so a long answer costs about as much
/// as reading it. The shared vectors hold what the comparison answers (ROADMAP 4.8h) and show that both still mean what
/// they meant.
/// </summary>
public sealed class LogicCostTests
{
    private static readonly EvaluationContext Context =
        new("2026-10-25T10:00:00Z", "UTC", "en", new Member(false, []), new Dictionary<string, IReadOnlyList<string>>(), []);

    [Test]
    public void ARuleComparesANumberAnswerOf10000Digits_InUnder50Milliseconds()
    {
        var answer = "7" + new string('3', 9_999);
        var larger = "7" + new string('3', 9_998) + "4";
        var others = Enumerable.Range(1, 19).Select(i => (Operand)new TextOperand(i + new string('0', 9_999))).ToList();
        var subject = new Subject(
            new Dictionary<string, FieldKind> { ["amount"] = FieldKind.Number },
            new Dictionary<string, IReadOnlyList<string>> { ["amount"] = [answer] },
            Context);
        LogicCondition[] conditions =
        [
            new("amount", null, "equals", new TextOperand(answer + ".000")),
            new("amount", null, "isOneOf", new ListOperand([.. others, new TextOperand(answer)])),
            new("amount", null, "lessThan", new TextOperand(larger)),
            new("amount", null, "between", new ListOperand([new TextOperand("0.5"), new TextOperand(larger)])),
            new("amount", null, "greaterThan", new TextOperand("-" + larger)),
        ];
        bool[] Evaluate() => [.. conditions.Select(condition => Rules.EvaluateCondition(condition, subject))];

        Evaluate(); // the first run compiles what it runs
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var results = Evaluate();
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.True);
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(50)), "five conditions over a 10,000-digit answer, one against 20 operands as long");

            // Cleaning returns a text with nothing to replace as it is, and comparing builds no number and no key, so
            // the run allocates its lists, not one copy of a value.
            Assert.That(allocated, Is.LessThan(answer.Length * sizeof(char)), "what the run allocates, in bytes");
        });
    }

    [TestCase("isOneOf")]
    [TestCase("contains")]
    public void ATextCondition_FoldsTheAnswerOnce_HoweverManyOperands(string op)
    {
        // An answer that folds, so each fold of it makes a new copy of 20,000 bytes and more (ROADMAP 4.8h).
        var answer = new string('A', 10_000);
        long Against(int count)
        {
            var others = Enumerable.Range(1, count).Select(i => "x" + i).ToList();
            var subject = new Subject(
                new Dictionary<string, FieldKind> { ["name"] = FieldKind.Text, ["others"] = FieldKind.Text },
                new Dictionary<string, IReadOnlyList<string>> { ["name"] = [answer], ["others"] = others },
                Context);
            Operand operand = op == "isOneOf" ? new ListOperand([.. others.Select(other => (Operand)new TextOperand(other))]) : new FieldOperand("others");
            var condition = new LogicCondition("name", null, op, operand);
            var matched = true;
            var bytes = Allocated(() => matched = Rules.EvaluateCondition(condition, subject));
            Assert.That(matched, Is.False, $"{op} against {count}");
            return bytes;
        }

        var one = Against(1);
        var twenty = Against(20);

        Assert.That(twenty - one, Is.LessThan(answer.Length), "what 19 more operands cost, in bytes: their own folds, not 19 more of the answer");
    }

    [Test]
    public void AMatchesCondition_CompilesItsPatternOnce()
    {
        const string Pattern = "(ab|cd|ef){300}";
        var subject = new Subject(
            new Dictionary<string, FieldKind> { ["phone"] = FieldKind.Text },
            new Dictionary<string, IReadOnlyList<string>> { ["phone"] = ["ab"] },
            Context);
        var compiled = SafePattern.Compile(Pattern);
        var compile = Allocated(() => SafePattern.Compile(Pattern));
        var run = Allocated(() => compiled.IsMatch("ab"));
        var condition = new LogicCondition("phone", null, "matches", new TextOperand(Pattern));
        LogicItem[] twice = [condition, condition with { }];
        var rule = new LogicRule("show", Enabled: true, "any", twice);

        Assert.Multiple(() =>
        {
            Assert.That(Allocated(() => Rules.EvaluateCondition(condition, subject)), Is.LessThan(compile + run + (compile / 2)), "one compile and one run, with room for the condition's own lists");
            Assert.That(Allocated(() => Rules.EvaluateRule(rule, subject)), Is.LessThan(compile + (2 * run) + (compile / 2)), "a rule that names the pattern twice compiles it once");
        });
    }

    [Test]
    public void APatternRun_KeepsOneSetOfBuffers_HoweverLongTheAnswer()
    {
        var pattern = SafePattern.Compile("(a|b|c)*[a-z]{1,3}");
        var value = new string('a', 10_000);
        pattern.IsMatch(value);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var matched = pattern.IsMatch(value);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(matched, Is.True);

            // Reading the code points takes about twice their size; the run's own buffers are a few hundred bytes.
            Assert.That(allocated, Is.LessThan(3L * value.Length * sizeof(int)));
        });
    }

    // What the action allocates on this thread, once it has run once.
    private static long Allocated(Action action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
