using System.Collections.Frozen;
using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// Calculations (section 4.3.3, ADR-0063), the same in TypeScript (<c>packages/logic/src/calc.ts</c>): a formula an
/// editor types, parsed into a tree of numbers, fields' aliases and the language's operations, and worked out over the
/// exact decimals of ADR-0005. Nothing in a formula runs as code. The spike's vectors write the same trees by hand, with
/// <c>store</c> for a number as a field stores it.
/// </summary>
internal static class Calculations
{
    /// <summary>The longest formula, in UTF-16 code units.</summary>
    public const int MaxLength = 1000;

    /// <summary>How deeply parentheses and calls nest in a formula.</summary>
    public const int MaxDepth = 20;

    private static readonly ExactDecimal Zero = ExactDecimal.Integer(0);
    private static readonly ExactDecimal One = ExactDecimal.Integer(1);

    /// <summary>Nothing to read: what a calculation without fields sees.</summary>
    private static readonly FormulaScope Nothing = new(_ => null, _ => null, () => throw new LogicException(LogicErrors.InvalidContext));

    /// <summary>The kinds a formula reads as numbers, and those it reads as dates.</summary>
    private static readonly FrozenSet<FieldKind> Numbers = new[] { FieldKind.Number, FieldKind.Choice, FieldKind.Choices, FieldKind.Checkbox }.ToFrozenSet();

    private static readonly FrozenSet<FieldKind> Dates = new[] { FieldKind.Date, FieldKind.DateTime }.ToFrozenSet();

    private static readonly FrozenDictionary<string, Func<ExactDecimal, ExactDecimal, ExactDecimal>> Binary =
        new Dictionary<string, Func<ExactDecimal, ExactDecimal, ExactDecimal>>(StringComparer.Ordinal)
        {
            ["add"] = ExactDecimal.Add,
            ["sub"] = ExactDecimal.Subtract,
            ["mul"] = ExactDecimal.Multiply,
            ["div"] = ExactDecimal.Divide,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Func<int, bool>> Comparisons =
        new Dictionary<string, Func<int, bool>>(StringComparer.Ordinal)
        {
            ["eq"] = order => order == 0,
            ["ne"] = order => order != 0,
            ["lt"] = order => order < 0,
            ["le"] = order => order <= 0,
            ["gt"] = order => order > 0,
            ["ge"] = order => order >= 0,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The fewest and the most arguments of each operation that is not binary.</summary>
    private static readonly FrozenDictionary<string, (int Min, int Max)> Arity =
        new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal)
        {
            ["neg"] = (1, 1),
            ["round"] = (1, 1),
            ["store"] = (1, 1),
            ["min"] = (2, int.MaxValue),
            ["max"] = (2, int.MaxValue),
            ["if"] = (3, 3),
            ["and"] = (2, int.MaxValue),
            ["or"] = (2, int.MaxValue),
            ["not"] = (1, 1),
            ["daysBetween"] = (2, 2),
            ["yearsBetween"] = (2, 2),
            ["today"] = (0, 0),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The functions a formula may call, with their fewest and most arguments as written.</summary>
    private static readonly FrozenDictionary<string, (int Min, int Max)> Functions =
        new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal)
        {
            ["min"] = (2, int.MaxValue),
            ["max"] = (2, int.MaxValue),
            ["round"] = (1, 2),
            ["if"] = (3, 3),
            ["and"] = (2, int.MaxValue),
            ["or"] = (2, int.MaxValue),
            ["not"] = (1, 1),
            ["daysBetween"] = (2, 2),
            ["yearsBetween"] = (2, 2),
            ["today"] = (0, 0),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DateFunctions = new[] { "daysBetween", "yearsBetween" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Reads a formula into its tree: the grammar, the depth, the numbers' range, the functions and their arguments
    /// (ADR-0063).
    /// </summary>
    /// <exception cref="LogicException">A mistake, with the position where it starts.</exception>
    public static Calculation Parse(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        var tree = new Parser(formula).Parse();
        PlaceToday(tree);
        return tree;
    }

    /// <summary>
    /// Checks the fields a formula names against the form: each alias must be a field the scope can read, a number where
    /// a number goes and a date in a date function, and the formula must name one.
    /// </summary>
    /// <returns>The aliases, in the order first named.</returns>
    /// <exception cref="LogicException">A mistake, with the position where it starts.</exception>
    public static IReadOnlyList<string> Check(Calculation calculation, Func<string, FieldKind?> kind)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentNullException.ThrowIfNull(kind);
        var named = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Visit(Calculation item, bool date)
        {
            if (item is FieldReference reference)
            {
                var found = kind(reference.Alias) ?? throw Mistake(LogicErrors.UnknownField, reference.At);
                if (!(date ? Dates : Numbers).Contains(found))
                {
                    throw Mistake(LogicErrors.OperatorNotForKind, reference.At);
                }

                if (seen.Add(reference.Alias))
                {
                    named.Add(reference.Alias);
                }
            }
            else if (item is Operation operation)
            {
                var dates = DateFunctions.Contains(operation.Op);
                foreach (var arg in operation.Args)
                {
                    Visit(arg, dates);
                }
            }
        }

        Visit(calculation, date: false);
        return named.Count == 0 ? throw new LogicException(LogicErrors.ConstantFormula, 0) : named;
    }

    /// <summary>
    /// Works out a calculation over a scope: null when an answer it reads does not read, or a date it needs is empty
    /// (ADR-0063). Each operation's shape is checked, then its arguments run left to right; <c>if</c>, <c>and</c> and
    /// <c>or</c> stop as soon as they know the answer.
    /// </summary>
    /// <exception cref="LogicException">A broken tree, or a failure of the arithmetic (<c>division_by_zero</c>, <c>out_of_range</c>).</exception>
    public static ExactDecimal? Calculate(Calculation calculation, FormulaScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        scope ??= Nothing;
        switch (calculation)
        {
            case Constant constant:
                return ExactDecimal.Operand(constant.Text);
            case FieldReference reference:
                return NumberOf(reference, scope);
        }

        var operation = (Operation)calculation;
        var (op, args) = (operation.Op, operation.Args);
        Binary.TryGetValue(op, out var binary);
        Comparisons.TryGetValue(op, out var comparison);
        if (binary is not null || comparison is not null)
        {
            if (args.Count != 2)
            {
                throw new LogicException(LogicErrors.InvalidOperand);
            }

            if (Calculate(args[0], scope) is not { } a || Calculate(args[1], scope) is not { } b)
            {
                return null;
            }

            return binary is not null ? binary(a, b) : comparison!(ExactDecimal.Compare(a, b)) ? One : Zero;
        }

        if (!Arity.TryGetValue(op, out var arity))
        {
            throw new LogicException(LogicErrors.UnknownOperator);
        }

        if (args.Count < arity.Min || args.Count > arity.Max)
        {
            throw new LogicException(LogicErrors.InvalidOperand);
        }

        ExactDecimal? Value(int k) => Calculate(args[k], scope);
        switch (op)
        {
            case "neg":
                return Value(0) is { } negated ? ExactDecimal.Negate(negated) : null;
            case "round":
                if (operation.Scale is not { } scale || scale < 0 || scale > ExactDecimal.MaxRoundPlaces)
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                return Value(0) is { } rounded ? ExactDecimal.Round(rounded, scale) : null;
            case "store":
                return Value(0) is { } kept ? ExactDecimal.Stored(kept) : null;
            case "min":
            case "max":
                ExactDecimal? best = null;
                for (var k = 0; k < args.Count; k++)
                {
                    if (Value(k) is not { } a)
                    {
                        return null;
                    }

                    if (best is not { } current || (op == "min" ? ExactDecimal.Compare(a, current) < 0 : ExactDecimal.Compare(a, current) > 0))
                    {
                        best = a;
                    }
                }

                return best;
            case "if":
                return Value(0) is { } condition ? Value(condition.N.IsZero ? 2 : 1) : null;
            case "and":
            case "or":
                for (var k = 0; k < args.Count; k++)
                {
                    if (Value(k) is not { } a)
                    {
                        return null;
                    }

                    if (!a.N.IsZero == (op == "or"))
                    {
                        return op == "or" ? One : Zero;
                    }
                }

                return op == "or" ? Zero : One;
            case "not":
                return Value(0) is { } inverted ? (inverted.N.IsZero ? One : Zero) : null;
            case "daysBetween":
            case "yearsBetween":
                if (DateOf(args[0], scope) is not { } from || DateOf(args[1], scope) is not { } to)
                {
                    return null;
                }

                return ExactDecimal.Integer(op == "daysBetween" ? to - from : YearsFrom(from, to));
            default:
                // today() is a date, never a number.
                throw new LogicException(LogicErrors.InvalidOperand);
        }
    }

    /// <summary>
    /// Reads, checks and works out a formula over a scope (ADR-0063): canonical text, or null without a value.
    /// </summary>
    /// <exception cref="LogicException">A mistake in the formula, with its position, or a failure of the arithmetic, without.</exception>
    public static string? Evaluate(string formula, FormulaScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var tree = Parse(formula);
        Check(tree, scope.Kind);
        return Calculate(tree, scope)?.ToString();
    }

    /// <summary>A number as a field stores it: the wire form within the storage limits, in canonical form.</summary>
    public static string StoredNumber(string text) =>
        ExactDecimal.Stored(ExactDecimal.Parse(text) ?? throw new LogicException(LogicErrors.NotANumber)).ToString();

    private static LogicException Mistake(string code, int? at) => at is { } position ? new LogicException(code, position) : new LogicException(code);

    private static bool IsToday(Calculation calculation) => calculation is Operation { Op: "today" };

    // today() only as a date function's argument; a function's own arguments were checked as it was read.
    private static void PlaceToday(Calculation calculation)
    {
        if (calculation is not Operation operation)
        {
            return;
        }

        if (operation.Op == "today")
        {
            throw Mistake(LogicErrors.InvalidOperand, operation.At);
        }

        if (DateFunctions.Contains(operation.Op))
        {
            return;
        }

        foreach (var arg in operation.Args)
        {
            PlaceToday(arg);
        }
    }

    // A field's answers as a number: their sum, none being 0; null when one does not read, or its type could not read them.
    private static ExactDecimal? NumberOf(FieldReference reference, FormulaScope scope)
    {
        var kind = scope.Kind(reference.Alias) ?? throw Mistake(LogicErrors.UnknownField, reference.At);
        if (!Numbers.Contains(kind))
        {
            throw Mistake(LogicErrors.OperatorNotForKind, reference.At);
        }

        if (scope.Unreadable?.Invoke(reference.Alias) == true)
        {
            return null;
        }

        var sum = Zero;
        foreach (var answer in scope.Answers(reference.Alias) ?? [])
        {
            var text = TextSemantics.Clean(answer);
            if (text.Length == 0)
            {
                continue;
            }

            var value = kind == FieldKind.Checkbox ? (text == "true" ? One : null) : ExactDecimal.ReadNumber(text);
            if (value is not { } number)
            {
                return null;
            }

            sum = ExactDecimal.Add(sum, number);
        }

        return sum;
    }

    // A date function's argument as a day number: the field's one date or today; null when there is none.
    private static int? DateOf(Calculation calculation, FormulaScope scope)
    {
        if (IsToday(calculation))
        {
            return scope.Today();
        }

        if (calculation is not FieldReference reference)
        {
            throw new LogicException(LogicErrors.InvalidOperand);
        }

        var kind = scope.Kind(reference.Alias) ?? throw Mistake(LogicErrors.UnknownField, reference.At);
        if (!Dates.Contains(kind))
        {
            throw Mistake(LogicErrors.OperatorNotForKind, reference.At);
        }

        if (scope.Unreadable?.Invoke(reference.Alias) == true)
        {
            return null;
        }

        var answers = (scope.Answers(reference.Alias) ?? []).Select(TextSemantics.Clean).Where(text => text.Length > 0).ToList();
        if (answers.Count != 1)
        {
            return null;
        }

        var text = answers[0];
        if (kind == FieldKind.Date)
        {
            return CalendarValues.ParseDate(text);
        }

        return CalendarValues.ParseDateTime(text) is null ? null : CalendarValues.ParseDate(text[..10]);
    }

    // Whole years from one date to another, as an age counts them; from 29 February a year is complete on 1 March.
    private static int YearsFrom(int from, int to)
    {
        if (to < from)
        {
            return -YearsFrom(to, from);
        }

        var a = DateOnly.FromDayNumber(from);
        var b = DateOnly.FromDayNumber(to);
        var years = b.Year - a.Year;
        return (b.Month * 100) + b.Day < (a.Month * 100) + a.Day ? years - 1 : years;
    }

    /// <summary>Reads a formula by the grammar of ADR-0063, left to right, reporting the first mistake where it starts.</summary>
    private sealed class Parser(string text)
    {
        private int _position;
        private int _depth;

        public Calculation Parse()
        {
            if (text.Length > MaxLength)
            {
                throw new LogicException(LogicErrors.InvalidFormula, MaxLength);
            }

            var tree = Comparison();
            return Peek() == -1 ? tree : throw new LogicException(LogicErrors.InvalidFormula, _position);
        }

        private static bool IsSpace(int c) => c is ' ' or 9 or 10 or 13;

        // The next character after any spaces, or -1 at the end; the position is left on it.
        private int Peek()
        {
            while (_position < text.Length && IsSpace(text[_position]))
            {
                _position++;
            }

            return _position < text.Length ? text[_position] : -1;
        }

        private char At(int index) => index < text.Length ? text[index] : (char)0;

        private Calculation Comparison()
        {
            var left = Sum();
            var c = Peek();
            var at = _position;
            var next = At(at + 1);
            string op;
            if (c == '=')
            {
                op = "eq";
            }
            else if (c == '!' && next == '=')
            {
                op = "ne";
            }
            else if (c == '<')
            {
                op = next == '=' ? "le" : "lt";
            }
            else if (c == '>')
            {
                op = next == '=' ? "ge" : "gt";
            }
            else
            {
                return left;
            }

            _position += op is "ne" or "le" or "ge" ? 2 : 1;
            return new Operation(op, [left, Sum()], null, at);
        }

        private Calculation Sum()
        {
            var left = Product();
            while (Peek() is '+' or '-')
            {
                var at = _position++;
                left = new Operation(text[at] == '+' ? "add" : "sub", [left, Product()], null, at);
            }

            return left;
        }

        private Calculation Product()
        {
            var left = Unary();
            while (Peek() is '*' or '/')
            {
                var at = _position++;
                left = new Operation(text[at] == '*' ? "mul" : "div", [left, Unary()], null, at);
            }

            return left;
        }

        // Negations are counted rather than nested, so a run of them costs nothing.
        private Calculation Unary()
        {
            var negations = 0;
            var at = 0;
            while (Peek() == '-')
            {
                if (negations == 0)
                {
                    at = _position;
                }

                negations++;
                _position++;
            }

            var value = Primary();
            return negations % 2 == 1 ? new Operation("neg", [value], null, at) : value;
        }

        private Calculation Primary()
        {
            var c = Peek();
            var start = _position;
            if (c is >= '0' and <= '9')
            {
                return Number();
            }

            if (c == '(')
            {
                Enter(start);
                _position++;
                var inner = Comparison();
                Close();
                return inner;
            }

            if (c != -1 && char.IsAsciiLetter((char)c))
            {
                while (_position < text.Length && char.IsAsciiLetterOrDigit(text[_position]))
                {
                    _position++;
                }

                var name = text[start.._position];
                return Peek() == '(' ? Call(name, start) : new FieldReference(name, start);
            }

            throw new LogicException(LogicErrors.InvalidFormula, start);
        }

        // The wire form without a sign: no leading zero, and at least one digit after a point.
        private Constant Number()
        {
            var start = _position;
            if (text[start] == '0' && char.IsAsciiDigit(At(start + 1)))
            {
                throw new LogicException(LogicErrors.InvalidFormula, start);
            }

            var i = start;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            if (At(i) == '.')
            {
                var fraction = ++i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i == fraction)
                {
                    throw new LogicException(LogicErrors.InvalidFormula, i);
                }
            }

            _position = i;
            var literal = text[start..i];
            try
            {
                ExactDecimal.ReadNumber(literal);
            }
            catch (LogicException exception)
            {
                throw new LogicException(exception.Code, start);
            }

            return new Constant(literal);
        }

        private Operation Call(string name, int start)
        {
            if (!Functions.TryGetValue(name, out var arity))
            {
                throw new LogicException(LogicErrors.UnknownOperator, start);
            }

            Enter(start);
            _position++;
            var args = new List<Calculation>();
            var starts = new List<int>();
            if (Peek() != ')')
            {
                while (true)
                {
                    Peek();
                    starts.Add(_position);
                    args.Add(Comparison());
                    if (Peek() != ',')
                    {
                        break;
                    }

                    _position++;
                }
            }

            Close();
            if (args.Count < arity.Min || args.Count > arity.Max)
            {
                throw new LogicException(LogicErrors.InvalidOperand, start);
            }

            if (DateFunctions.Contains(name))
            {
                for (var k = 0; k < args.Count; k++)
                {
                    if (args[k] is not FieldReference && !IsToday(args[k]))
                    {
                        throw new LogicException(LogicErrors.InvalidOperand, starts[k]);
                    }
                }
            }

            if (name != "round")
            {
                return new Operation(name, args, null, start);
            }

            if (args.Count == 1)
            {
                return new Operation("round", [args[0]], 0, start);
            }

            // Places are a whole number written as it is: the literal has no sign, and no leading zero.
            if (args[1] is not Constant places || !int.TryParse(places.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var scale)
                || scale > ExactDecimal.MaxRoundPlaces)
            {
                throw new LogicException(LogicErrors.InvalidOperand, starts[1]);
            }

            return new Operation("round", [args[0]], scale, start);
        }

        private void Enter(int at)
        {
            if (++_depth > MaxDepth)
            {
                throw new LogicException(LogicErrors.TooDeep, at);
            }
        }

        private void Close()
        {
            if (Peek() != ')')
            {
                throw new LogicException(LogicErrors.InvalidFormula, _position);
            }

            _position++;
            _depth--;
        }
    }
}
