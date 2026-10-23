namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// Patterns (ADR-0005): a small subset of regular-expression syntax that Jiron Form Builder runs with its own matcher,
/// the same algorithm in TypeScript. A pattern is parsed (everything outside the subset is refused), compiled to a
/// Thompson automaton and run over the value's code points in time linear in the value's length, so no pattern can hang
/// a browser or a server, and no engine's dialect decides what a pattern means. A pattern always matches the whole
/// value; it is case-sensitive.
/// </summary>
internal sealed class SafePattern
{
    /// <summary>The longest pattern, in code points.</summary>
    public const int MaxPatternLength = 500;

    /// <summary>The largest count a <c>{n,m}</c> quantifier may name.</summary>
    public const int MaxCount = 1000;

    /// <summary>The largest pattern once counted repeats are unrolled, in matched characters.</summary>
    public const int MaxWeight = 2000;

    /// <summary>
    /// The largest automaton, in instructions: bounds the work per character, also for repeats of what matches no
    /// character (anchors, empty groups), which <see cref="MaxWeight"/> does not count (ADR-0030).
    /// </summary>
    public const int MaxInstructions = 10000;

    private const int MaxCodePoint = 0x10FFFF;

    // The automaton as a program of (op, a, b) instructions.
    private const int Char = 0; // a: index of the set; then the next instruction
    private const int Split = 1; // continue at a and at b
    private const int Jump = 2; // continue at a
    private const int Start = 3; // only at the first position; then the next instruction
    private const int End = 4; // only after the last position; then the next instruction
    private const int Match = 5;

    private static readonly Range[] Digits = [new(48, 57)];
    private static readonly Range[] Word = [new(48, 57), new(65, 90), new(95, 95), new(97, 122)];
    private static readonly Range[] Space = [new(9, 13), new(32, 32)];
    private static readonly Range[] LineEnds = [new(10, 10), new(13, 13), new(0x2028, 0x2029)];

    private readonly List<(int Op, int A, int B)> _code;
    private readonly List<CharSet> _sets;

    private SafePattern(List<(int Op, int A, int B)> code, List<CharSet> sets)
    {
        _code = code;
        _sets = sets;
    }

    /// <summary>Parses and compiles an editor's pattern, or refuses it with <c>unsafe_pattern</c>.</summary>
    public static SafePattern Compile(string pattern)
    {
        var points = CodePoints(TextSemantics.Normalise(pattern));
        if (points.Length > MaxPatternLength)
        {
            throw Refuse();
        }

        var parser = new Parser(points);
        var root = parser.Alternation();
        if (!parser.Done)
        {
            throw Refuse(); // an unmatched ")"
        }

        if (Weight(root) > MaxWeight || Size(root) > MaxInstructions)
        {
            throw Refuse();
        }

        var compiler = new Compiler();
        compiler.Walk(root);
        compiler.Emit(Match);
        return new SafePattern(compiler.Code, compiler.Sets);
    }

    /// <summary>Whether the whole value matches.</summary>
    public bool IsMatch(string value) => Run(CodePoints(value));

    private static LogicException Refuse() => new(LogicErrors.UnsafePattern);

    private static int[] CodePoints(string text)
    {
        var points = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            points.Add(rune.Value);
        }

        return [.. points];
    }

    private static bool IsAlphanumeric(int c) => c is (>= 48 and <= 57) or (>= 65 and <= 90) or (>= 97 and <= 122);

    private static int HexValue(int c) => c switch
    {
        >= 48 and <= 57 => c - 48,
        >= 65 and <= 70 => c - 55,
        >= 97 and <= 102 => c - 87,
        _ => -1,
    };

    /// <summary>The ranges outside the given ones, within all code points.</summary>
    private static Range[] Complement(Range[] ranges)
    {
        var output = new List<Range>();
        var next = 0;
        foreach (var range in ranges.OrderBy(r => r.Low))
        {
            if (range.Low > next)
            {
                output.Add(new Range(next, range.Low - 1));
            }

            next = Math.Max(next, range.High + 1);
        }

        if (next <= MaxCodePoint)
        {
            output.Add(new Range(next, MaxCodePoint));
        }

        return [.. output];
    }

    /// <summary>Caps a count just past its limit, so nested repeats can never overflow while they are added up.</summary>
    private static long Cap(long value, int limit) => Math.Min(value, limit + 1L);

    /// <summary>How many characters the node matches once counted repeats are unrolled, capped past <see cref="MaxWeight"/>.</summary>
    private static long Weight(Node node) => node switch
    {
        SetNode => 1,
        SeqNode seq => seq.Items.Aggregate(0L, (sum, item) => Cap(sum + Weight(item), MaxWeight)),
        AltNode alt => alt.Options.Aggregate(0L, (sum, option) => Cap(sum + Weight(option), MaxWeight)),
        RepeatNode repeat => Cap(Weight(repeat.Node) * Math.Max(repeat.Max ?? repeat.Min, 1), MaxWeight),
        _ => 0,
    };

    /// <summary>How many instructions the compiler emits for the node, capped past <see cref="MaxInstructions"/>.</summary>
    private static long Size(Node node)
    {
        switch (node)
        {
            case SeqNode seq:
                return seq.Items.Aggregate(0L, (sum, item) => Cap(sum + Size(item), MaxInstructions));
            case AltNode alt:
                // Every option but the last brings a split and a jump.
                var options = alt.Options.Aggregate(0L, (sum, option) => Cap(sum + Size(option), MaxInstructions));
                return Cap(options + (2L * (alt.Options.Count - 1)), MaxInstructions);
            case RepeatNode { Max: null } repeat:
                // x{n,} is n copies and a split; x* is one copy, a split and a jump.
                var copies = Math.Max(repeat.Min, 1);
                return Cap((Size(repeat.Node) * copies) + (repeat.Min > 0 ? 1 : 2), MaxInstructions);
            case RepeatNode repeat:
                // x{n,m} is m copies, of which the last m - n each start with a split.
                var max = repeat.Max.Value;
                return Cap((Size(repeat.Node) * max) + (max - repeat.Min), MaxInstructions);
            default:
                return 1; // a set, a start or an end
        }
    }

    /// <summary>Runs the automaton over the code points: every thread advances together, so time is linear in the length.</summary>
    private bool Run(int[] points)
    {
        var seen = new int[_code.Count];
        Array.Fill(seen, -1);
        var generation = 0;

        List<int> Closure(List<int> starts, int position)
        {
            generation++;
            var threads = new List<int>();
            var stack = new Stack<int>(starts.Count);
            for (var k = starts.Count - 1; k >= 0; k--)
            {
                stack.Push(starts[k]);
            }

            while (stack.Count > 0)
            {
                var pc = stack.Pop();
                if (seen[pc] == generation)
                {
                    continue;
                }

                seen[pc] = generation;
                var (op, a, b) = _code[pc];
                switch (op)
                {
                    case Split:
                        stack.Push(b);
                        stack.Push(a);
                        break;
                    case Jump:
                        stack.Push(a);
                        break;
                    case Start:
                        if (position == 0)
                        {
                            stack.Push(pc + 1);
                        }

                        break;
                    case End:
                        if (position == points.Length)
                        {
                            stack.Push(pc + 1);
                        }

                        break;
                    default:
                        threads.Add(pc);
                        break;
                }
            }

            return threads;
        }

        var threads = Closure([0], 0);
        for (var position = 0; position < points.Length && threads.Count > 0; position++)
        {
            var point = points[position];
            var advanced = new List<int>(threads.Count);
            foreach (var pc in threads)
            {
                if (_code[pc].Op == Char && _sets[_code[pc].A].Contains(point))
                {
                    advanced.Add(pc + 1);
                }
            }

            threads = Closure(advanced, position + 1);
        }

        return threads.Exists(pc => _code[pc].Op == Match);
    }

    private readonly record struct Range(int Low, int High);

    private sealed record CharSet(Range[] Ranges, bool Negated)
    {
        public bool Contains(int point) => Array.Exists(Ranges, r => point >= r.Low && point <= r.High) != Negated;
    }

    private abstract record Node;

    private sealed record SetNode(CharSet Set) : Node;

    private sealed record SeqNode(List<Node> Items) : Node;

    private sealed record AltNode(List<Node> Options) : Node;

    private sealed record RepeatNode(Node Node, int Min, int? Max) : Node;

    private sealed record StartNode : Node;

    private sealed record EndNode : Node;

    /// <summary>What an escape or class member stands for: one code point, or a set of ranges.</summary>
    private readonly record struct Escaped(int Point, Range[]? Ranges, bool Raw);

    private sealed class Parser(int[] points)
    {
        private int _i;

        public bool Done => _i >= points.Length;

        /// <summary>alternation: sequence ("|" sequence)*</summary>
        public Node Alternation()
        {
            var options = new List<Node> { Sequence() };
            while (Peek() == '|')
            {
                Next();
                options.Add(Sequence());
            }

            return options.Count == 1 ? options[0] : new AltNode(options);
        }

        private int Peek(int offset = 0) => _i + offset < points.Length ? points[_i + offset] : -1;

        private int Next() => Done ? throw Refuse() : points[_i++];

        private SeqNode Sequence()
        {
            var items = new List<Node>();
            while (!Done && Peek() != '|' && Peek() != ')')
            {
                var atom = Atom();
                items.Add(atom is StartNode or EndNode ? atom : Quantified(atom));
            }

            return new SeqNode(items);
        }

        private Node Atom()
        {
            var c = Next();
            switch (c)
            {
                case '(':
                    if (Peek() == '?')
                    {
                        Next();
                        if (Next() != ':')
                        {
                            throw Refuse(); // only "(?:" among the "(?" forms
                        }
                    }

                    var inner = Alternation();
                    return Next() == ')' ? inner : throw Refuse();
                case '[':
                    return CharacterClass();
                case '.':
                    return new SetNode(new CharSet(LineEnds, true));
                case '\\':
                    var escaped = Escape();
                    return new SetNode(new CharSet(escaped.Ranges ?? [new Range(escaped.Point, escaped.Point)], false));
                case '^':
                    return new StartNode();
                case '$':
                    return new EndNode();
                case '*' or '+' or '?' or '{':
                    throw Refuse(); // nothing to repeat
                case ']' or '}':
                    throw Refuse(); // a lone "]" or "}" must be escaped
                default:
                    return new SetNode(new CharSet([new Range(c, c)], false));
            }
        }

        private Node Quantified(Node atom)
        {
            var c = Peek();
            int min;
            int? max;
            switch (c)
            {
                case '*':
                    (min, max) = (0, null);
                    break;
                case '+':
                    (min, max) = (1, null);
                    break;
                case '?':
                    (min, max) = (0, 1);
                    break;
                case '{':
                    Next();
                    (min, max) = Counts();
                    break;
                default:
                    return atom;
            }

            if (c != '{')
            {
                Next();
            }

            if (Peek() == '?')
            {
                Next(); // a lazy quantifier matches the same values
            }

            // A second quantifier now finds nothing to repeat, so stacked quantifiers are refused by Atom().
            return new RepeatNode(atom, min, max);
        }

        /// <summary>Reads "n}", "n,}" or "n,m}" after a "{", up to and including the "}".</summary>
        private (int Low, int? High) Counts()
        {
            int? Number()
            {
                var digits = 0;
                var value = 0;
                while (Peek() is >= '0' and <= '9' && digits < 5)
                {
                    value = (value * 10) + (Next() - '0');
                    digits++;
                }

                return digits == 0 ? null : value;
            }

            var low = Number() ?? throw Refuse();
            int? high = low;
            if (Peek() == ',')
            {
                Next();
                high = Peek() == '}' ? null : Number() ?? throw Refuse();
            }

            if (Next() != '}' || low > MaxCount || (high is { } h && (h > MaxCount || h < low)))
            {
                throw Refuse();
            }

            return (low, high);
        }

        private int Hex(int count)
        {
            var value = 0;
            for (var k = 0; k < count; k++)
            {
                var digit = HexValue(Next());
                if (digit < 0)
                {
                    throw Refuse();
                }

                value = (value * 16) + digit;
            }

            return value;
        }

        /// <summary>Reads what follows a backslash. Refuses backreferences, boundaries, properties and unknown letters.</summary>
        private Escaped Escape()
        {
            var c = Next();
            switch (c)
            {
                case 'd':
                    return new Escaped(0, Digits, false);
                case 'w':
                    return new Escaped(0, Word, false);
                case 's':
                    return new Escaped(0, Space, false);
                case 'D':
                    return new Escaped(0, Complement(Digits), false);
                case 'W':
                    return new Escaped(0, Complement(Word), false);
                case 'S':
                    return new Escaped(0, Complement(Space), false);
                case 't':
                    return new Escaped(9, null, false);
                case 'n':
                    return new Escaped(10, null, false);
                case 'v':
                    return new Escaped(11, null, false);
                case 'f':
                    return new Escaped(12, null, false);
                case 'r':
                    return new Escaped(13, null, false);
                case 'u' or 'x':
                    var point = Hex(c == 'u' ? 4 : 2);
                    return point is >= 0xD800 and <= 0xDFFF ? throw Refuse() : new Escaped(point, null, false);
                default:
                    return c is >= 32 and <= 126 && !IsAlphanumeric(c) ? new Escaped(c, null, false) : throw Refuse();
            }
        }

        /// <summary>A class member: an escape or a raw code point. A raw "[" is refused, as in no common dialect it is plain.</summary>
        private Escaped Member(int c) => c switch
        {
            '[' => throw Refuse(),
            '\\' => Escape(),
            _ => new Escaped(c, null, true),
        };

        /// <summary>
        /// Reads a class after its "[": code points, ranges between two code points, and the escaped sets. A raw "-"
        /// is a literal only first or last; anywhere else it must join a range, and it never ends one.
        /// </summary>
        private SetNode CharacterClass()
        {
            var negated = false;
            if (Peek() == '^')
            {
                Next();
                negated = true;
            }

            if (Peek() == ']')
            {
                throw Refuse(); // "[]" and "[^]" mean different things in common dialects
            }

            var ranges = new List<Range>();
            var first = true;
            while (true)
            {
                var c = Next();
                if (c == ']')
                {
                    return new SetNode(new CharSet([.. ranges], negated));
                }

                var item = Member(c);
                var hyphen = item.Ranges is null && item.Raw && item.Point == '-';
                var range = Peek() == '-' && Peek(1) != ']';
                if (hyphen && !first && Peek() != ']')
                {
                    throw Refuse();
                }

                first = false;
                if (!range)
                {
                    ranges.AddRange(item.Ranges ?? [new Range(item.Point, item.Point)]);
                    continue;
                }

                if (item.Ranges is not null || hyphen)
                {
                    throw Refuse(); // a set or a raw "-" cannot start a range
                }

                Next();
                var high = Member(Next());
                if (high.Ranges is not null || (high.Raw && high.Point == '-') || high.Point < item.Point)
                {
                    throw Refuse();
                }

                ranges.Add(new Range(item.Point, high.Point));
            }
        }
    }

    private sealed class Compiler
    {
        public List<(int Op, int A, int B)> Code { get; } = [];

        public List<CharSet> Sets { get; } = [];

        public int Emit(int op, int a = 0, int b = 0)
        {
            Code.Add((op, a, b));
            return Code.Count - 1;
        }

        public void Walk(Node node)
        {
            switch (node)
            {
                case SetNode set:
                    Sets.Add(set.Set);
                    Emit(Char, Sets.Count - 1);
                    return;
                case StartNode:
                    Emit(Start);
                    return;
                case EndNode:
                    Emit(End);
                    return;
                case SeqNode seq:
                    seq.Items.ForEach(Walk);
                    return;
                case AltNode alt:
                    WalkAlternation(alt);
                    return;
                case RepeatNode repeat:
                    WalkRepeat(repeat);
                    return;
            }
        }

        private void Patch(int at, int a, int? b = null) => Code[at] = (Code[at].Op, a, b ?? Code[at].B);

        private void WalkAlternation(AltNode alt)
        {
            var jumps = new List<int>();
            for (var index = 0; index < alt.Options.Count; index++)
            {
                if (index == alt.Options.Count - 1)
                {
                    Walk(alt.Options[index]);
                    continue;
                }

                var split = Emit(Split);
                Walk(alt.Options[index]);
                jumps.Add(Emit(Jump));
                Patch(split, split + 1, Code.Count);
            }

            jumps.ForEach(jump => Patch(jump, Code.Count));
        }

        private void WalkRepeat(RepeatNode repeat)
        {
            var (inner, min, max) = repeat;
            for (var k = 0; k < min - (max is null && min > 0 ? 1 : 0); k++)
            {
                Walk(inner);
            }

            if (max is null)
            {
                if (min > 0)
                {
                    var loop = Code.Count; // x+ : x, then back to x or on
                    Walk(inner);
                    var split = Emit(Split);
                    Patch(split, loop, split + 1);
                }
                else
                {
                    var split = Emit(Split); // x* : x and back, or on
                    Walk(inner);
                    Emit(Jump, split);
                    Patch(split, split + 1, Code.Count);
                }

                return;
            }

            var splits = new List<int>();
            for (var k = min; k < max; k++)
            {
                splits.Add(Emit(Split));
                Walk(inner);
            }

            splits.ForEach(split => Patch(split, split + 1, Code.Count));
        }
    }
}
