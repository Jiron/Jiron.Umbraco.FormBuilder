using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// What the logic costs on long answers (ROADMAP 4.8f): numbers compare digit by digit as the values they are, without
/// a number or a key being built, and a pattern's run keeps one set of buffers, so a long answer costs about as much
/// as reading it. The shared vectors show that both still mean what they meant.
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

    [TestCase("0", "-0", 0)]
    [TestCase("0.50", "0.5", 0)]
    [TestCase("-0.000", "0", 0)]
    [TestCase("10", "9", 1)]
    [TestCase("-10", "-9", -1)]
    [TestCase("1.25", "1.3", -1)]
    [TestCase("12", "12.0001", -1)]
    [TestCase("-0.5", "0", -1)]
    [TestCase("-1.5", "-1.25", -1)]
    [TestCase("123456789012345678901234567890", "123456789012345678901234567891", -1)]
    public void Numbers_CompareAsTheValuesTheyAre(string a, string b, int expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExactDecimal.CompareText(a, b), Is.EqualTo(expected));
            Assert.That(ExactDecimal.CompareText(b, a), Is.EqualTo(-expected), "the other way round");
        });
    }

    [TestCase("1e3")]
    [TestCase("01")]
    [TestCase("1.")]
    [TestCase(".5")]
    [TestCase("-")]
    [TestCase("")]
    [TestCase("+1")]
    [TestCase("1,5")]
    [TestCase("1.5x")]
    public void WhatIsNotANumberInTheWireForm_ComparesWithNothing(string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExactDecimal.CompareText(text, "1"), Is.Null);
            Assert.That(ExactDecimal.CompareText("1", text), Is.Null);
            Assert.That(ExactDecimal.Parse(text), Is.Null, "as the parser refuses it");
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
}
