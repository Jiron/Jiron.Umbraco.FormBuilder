using System.Diagnostics;
using System.Text;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Formulas an editor might type, and many no editor would (ROADMAP 5.3a, ADR-0063): 10,000 random formulas, built
/// from the grammar and then broken by chance, are read, checked and worked out over random answers. Each gives its
/// result or refuses with a <see cref="LogicException"/>, nothing else, and each takes at most 50 ms: the fastest of
/// three runs, so that a garbage collection or the first compilation of a path does not count against the formula.
/// The same in TypeScript (<c>packages/logic/test/formulas.test.ts</c>).
/// </summary>
public sealed class FormulaFuzzTests
{
    private const int Formulas = 10_000;
    private const int Seed = 5301;
    private const int Runs = 3;

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(50);

    private static readonly Dictionary<string, FieldKind> Kinds = new(StringComparer.Ordinal)
    {
        ["qty"] = FieldKind.Number,
        ["price"] = FieldKind.Number,
        ["size"] = FieldKind.Choice,
        ["extras"] = FieldKind.Choices,
        ["gift"] = FieldKind.Checkbox,
        ["arrival"] = FieldKind.Date,
        ["birth"] = FieldKind.DateTime,
        ["name"] = FieldKind.Text,
        ["photos"] = FieldKind.Files,
        ["start"] = FieldKind.Time,
    };

    private static readonly int Today = CalendarValues.Today("2026-10-26T10:00:00Z", "Europe/Zurich");

    [Test]
    public void TenThousandRandomFormulas_GiveAResultOrAMistake_EachInAtMost50Milliseconds()
    {
        var generator = new FormulaGenerator(Seed);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        var slowest = (Time: TimeSpan.Zero, Formula: string.Empty);
        for (var i = 0; i < Formulas; i++)
        {
            var formula = generator.Formula();
            var scope = Scope(generator.Answers());
            var fastest = TimeSpan.MaxValue;
            var outcome = string.Empty;
            for (var run = 0; run < Runs; run++)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    outcome = Calculations.Evaluate(formula, scope) is null ? "no value" : "value";
                }
                catch (LogicException exception)
                {
                    outcome = exception.Code;
                }
                catch (Exception exception)
                {
                    failures.Add($"formula {i}: {exception.GetType().Name} for {Fuzzing.Show(formula)}");
                    break;
                }

                watch.Stop();
                fastest = watch.Elapsed < fastest ? watch.Elapsed : fastest;
            }

            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            if (fastest != TimeSpan.MaxValue && fastest > Budget)
            {
                failures.Add($"formula {i}: {fastest.TotalMilliseconds:F1} ms for {Fuzzing.Show(formula)}");
            }

            if (fastest != TimeSpan.MaxValue && fastest > slowest.Time)
            {
                slowest = (fastest, formula);
            }
        }

        TestContext.Out.WriteLine(string.Join(", ", outcomes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}")));
        TestContext.Out.WriteLine($"slowest: {slowest.Time.TotalMilliseconds:F3} ms for {Fuzzing.Show(slowest.Formula)}");
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty);

            // The formulas reach every outcome, so the generator explores the language and not one corner of it.
            Assert.That(
                FormulaGenerator.Outcomes.Where(expected => outcomes.GetValueOrDefault(expected) < Formulas / 500),
                Is.Empty,
                "outcomes reached fewer than 20 times");
        });
    }

    [Test]
    public void TheGenerator_GivesTheSameFormulasForTheSameSeed()
    {
        var first = new FormulaGenerator(Seed);
        var second = new FormulaGenerator(Seed);

        Assert.That(Enumerable.Range(0, 100).Select(_ => first.Formula()), Is.EqualTo(Enumerable.Range(0, 100).Select(_ => second.Formula())));
    }

    [Test]
    public void TheCalculations_RefuseNoArguments()
    {
        var tree = Calculations.Parse("qty");
        var scope = Scope([]);

        Assert.Multiple(() =>
        {
            Assert.That(() => Calculations.Parse(null!), Throws.ArgumentNullException);
            Assert.That(() => Calculations.Check(null!, _ => FieldKind.Number), Throws.ArgumentNullException);
            Assert.That(() => Calculations.Check(tree, null!), Throws.ArgumentNullException);
            Assert.That(() => Calculations.Calculate(null!), Throws.ArgumentNullException);
            Assert.That(() => Calculations.Evaluate(null!, scope), Throws.ArgumentNullException);
            Assert.That(() => Calculations.Evaluate("qty", null!), Throws.ArgumentNullException);
        });
    }

    private static FormulaScope Scope(Dictionary<string, IReadOnlyList<string>?> answers) => new(
        alias => Kinds.TryGetValue(alias, out var kind) ? kind : null,
        alias => answers.GetValueOrDefault(alias),
        () => Today);

    /// <summary>Random formulas from the grammar of ADR-0063, broken now and then, and random answers to them.</summary>
    private sealed class FormulaGenerator(int seed)
    {
        /// <summary>What a formula can come to: a value, none, or each mistake and failure.</summary>
        public static readonly string[] Outcomes =
        [
            "value", "no value", LogicErrors.InvalidFormula, LogicErrors.UnknownOperator, LogicErrors.InvalidOperand, LogicErrors.TooDeep,
            LogicErrors.OutOfRange, LogicErrors.UnknownField, LogicErrors.OperatorNotForKind, LogicErrors.ConstantFormula, LogicErrors.DivisionByZero,
        ];

        // Mostly the fields arithmetic takes; now and then any field, or none the form has.
        private static readonly string[] Amounts = ["qty", "qty", "price", "price", "size", "extras", "gift"];
        private static readonly string[] Aliases = [.. Kinds.Keys, "unknown", "min", "Qty"];
        private static readonly string[] Numbers = ["0", "1", "2", "3", "10", "12", "0.5", "2.5", "99.99", "0.001"];
        private static readonly string[] OddNumbers = ["00", "1.", ".5", "1e5", "007", "1,5"];
        private static readonly string[] Functions = ["min", "max", "round", "if", "and", "or", "not", "daysBetween", "yearsBetween"];
        private static readonly string[] OddFunctions = ["today", "pow", "Round", "sum", "constructor"];
        private static readonly string[] Operators = ["+", "-", "*", "/", "=", "!=", "<", "<=", ">", ">="];
        private static readonly string[] Places = ["0", "0", "1", "2", "2", "3", "20", "21", "1.5", "99999999999"];
        private static readonly string[] Spaces = [" ", " ", " ", "  ", "\t", "\n", "\r\n"];

        // What a mistake puts into a formula: the language's own characters, and some it does not have.
        private static readonly string[] Noise =
        [
            "(", ")", ",", ".", "+", "-", "*", "/", "=", "!", "<", ">", " ", "0", "9", "a", "Z", "_", "$", "\"", "'", "%", "^", "&", "|",
            ((char)0xE9).ToString(), ((char)0x2212).ToString(), ((char)0xFF11).ToString(), ((char)0xD83D).ToString() + (char)0xDE00, ((char)0xD800).ToString(),
            ((char)0).ToString(),
        ];

        private static readonly string[][] AnswerChoices =
        [
            [], ["0"], ["1"], ["2.5"], ["-3"], ["12", "7"], ["0.333"], ["abc"], ["1e5"], [" 4 "], ["2026-10-26"], ["2024-02-29"],
            ["2026-10-26T23:30"], ["2026-13-01"], ["true"], ["false"], [new string('9', 60)], ["1" + new string('0', 59)], ["0." + new string('0', 30) + "1"],
        ];

        private readonly Random _random = new(seed);
        private int _budget;

        public string Formula()
        {
            var text = _random.Next(50) switch
            {
                // About the longest formula there is, a little shorter or longer than the limit.
                0 => Long(),

                // Nested as deep as the limit, or one level deeper.
                1 => Nested(Calculations.MaxDepth + _random.Next(-1, 2)),
                _ => Built(),
            };

            // Then, now and then, broken: characters dropped, put in or doubled.
            var mistakes = _random.Next(6) == 0 ? _random.Next(1, 4) : 0;
            for (var m = 0; m < mistakes; m++)
            {
                text = Break(text);
            }

            return text;
        }

        public Dictionary<string, IReadOnlyList<string>?> Answers()
        {
            var answers = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);
            foreach (var alias in Kinds.Keys)
            {
                // A hidden field has no answers at all.
                answers[alias] = _random.Next(8) == 0 ? null : Pick(AnswerChoices);
            }

            return answers;
        }

        private string Built()
        {
            _budget = _random.Next(1, 120);
            return Expression(0);
        }

        private string Long()
        {
            var terms = (Calculations.MaxLength / 6) + _random.Next(-2, 3);
            return string.Join(" + ", Enumerable.Repeat("qty", terms)) + new string(' ', _random.Next(4));
        }

        private string Nested(int levels)
        {
            var open = new StringBuilder();
            var close = new StringBuilder();
            for (var level = 0; level < levels; level++)
            {
                var call = _random.Next(3) == 0;
                open.Append(call ? "not(" : "(");
                close.Append(')');
            }

            return open + "qty" + close;
        }

        private string Expression(int depth)
        {
            if (--_budget <= 0 || depth > Calculations.MaxDepth + 2)
            {
                return Leaf();
            }

            return _random.Next(10) switch
            {
                0 or 1 or 2 => Leaf(),
                3 or 4 => Expression(depth) + Space() + Pick(Operators) + Space() + Expression(depth),
                5 => Expression(depth) + Space() + "/" + Space() + Expression(depth),
                6 => "(" + Space() + Expression(depth + 1) + Space() + ")",
                7 => new string('-', _random.Next(1, 4)) + Expression(depth),
                _ => Call(depth),
            };
        }

        private string Call(int depth)
        {
            var name = _random.Next(20) == 0 ? Pick(OddFunctions) : Pick(Functions);

            // Now and then the wrong number of arguments.
            var wrong = _random.Next(15) == 0;
            var count = name switch
            {
                "today" => wrong ? 1 : 0,
                "not" => wrong ? 2 : 1,
                "if" => wrong ? 2 : 3,
                "daysBetween" or "yearsBetween" => wrong ? 1 : 2,
                "round" => wrong ? 3 : _random.Next(1, 3),
                _ => wrong ? _random.Next(0, 2) : _random.Next(2, 5),
            };
            var args = new List<string>();
            for (var i = 0; i < count; i++)
            {
                args.Add((name, i) switch
                {
                    ("daysBetween" or "yearsBetween", _) => _random.Next(12) switch
                    {
                        0 or 1 or 2 => "today()",
                        3 or 4 or 5 or 6 => "arrival",
                        7 or 8 or 9 => "birth",
                        10 => Pick(Aliases),
                        _ => Expression(depth + 1),
                    },
                    ("round", 1) => _random.Next(8) == 0 ? Expression(depth + 1) : Pick(Places),
                    _ => Expression(depth + 1),
                });
            }

            return name + "(" + Space() + string.Join("," + Space(), args) + Space() + ")";
        }

        private string Leaf() => _random.Next(40) switch
        {
            < 14 => Pick(Numbers),
            14 => Pick(OddNumbers),
            15 or 16 => _random.Next(2) == 0 ? "1" + new string('0', _random.Next(55, 62)) : "0." + new string('3', _random.Next(1, 70)),
            17 => "today()",
            < 22 => Pick(Aliases),
            _ => Pick(Amounts),
        };

        private string Space() => _random.Next(3) == 0 ? Pick(Spaces) : string.Empty;

        private string Break(string text)
        {
            var at = _random.Next(text.Length + 1);
            return _random.Next(3) switch
            {
                0 when text.Length > 0 => text.Remove(Math.Min(at, text.Length - 1), 1),
                1 => text.Insert(at, Pick(Noise)),
                _ => text.Insert(at, text[at..Math.Min(text.Length, at + _random.Next(1, 8))]),
            };
        }

        private T Pick<T>(T[] items) => items[_random.Next(items.Length)];
    }
}
