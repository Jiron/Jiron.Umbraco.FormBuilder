using System.Globalization;
using System.Numerics;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// An exact decimal <c>N / 10^S</c> with <c>S &gt;= 0</c> and no trailing zeros in the fraction, so equal values look
/// alike (ADR-0005), the same in TypeScript. Neither runtime uses its own number type for answers: C# decimal and
/// JavaScript doubles round differently.
/// </summary>
internal readonly record struct ExactDecimal
{
    /// <summary>Digits a stored number may have in all (leading zeros don't count), and after the point (ADR-0003).</summary>
    public const int StoredDigits = 15;

    /// <summary>Places a division keeps, rounded half away from zero.</summary>
    public const int DivisionPlaces = 20;

    /// <summary>The most places <see cref="Round"/> takes.</summary>
    public const int MaxRoundPlaces = 20;

    /// <summary>Digits an intermediate result may have before a calculation is out of range.</summary>
    public const int MaxDigits = 60;

    private ExactDecimal(BigInteger n, int s)
    {
        N = n;
        S = s;
    }

    /// <summary>The digits, with the sign.</summary>
    public BigInteger N { get; }

    /// <summary>The places after the point.</summary>
    public int S { get; }

    /// <summary>
    /// Parses the wire form: an optional minus, an integer part without leading zeros, and an optional point followed
    /// by at least one digit. ASCII digits only; no plus sign, exponent, group separator or culture. Null otherwise.
    /// </summary>
    public static ExactDecimal? Parse(string text)
    {
        var negative = text.Length > 0 && text[0] == '-';
        var i = negative ? 1 : 0;
        var intEnd = ReadDigits(text, i);
        var intPart = text[i..intEnd];
        if (intPart.Length == 0 || (intPart.Length > 1 && intPart[0] == '0'))
        {
            return null;
        }

        var fraction = string.Empty;
        if (intEnd < text.Length)
        {
            if (text[intEnd] != '.')
            {
                return null;
            }

            var fractionEnd = ReadDigits(text, intEnd + 1);
            if (fractionEnd == intEnd + 1 || fractionEnd != text.Length)
            {
                return null;
            }

            // Trailing zeros go before the digits become a number, so a long run of them costs one pass.
            fraction = text[(intEnd + 1)..fractionEnd].TrimEnd('0');
        }

        var n = BigInteger.Parse(intPart + fraction, NumberStyles.None, CultureInfo.InvariantCulture);
        return new ExactDecimal(negative ? -n : n, fraction.Length);
    }

    /// <summary>
    /// Orders two values in the wire form as the numbers they are, without building either: by the sign, then the
    /// integer digits, then the fraction's digits without its trailing zeros. The work is linear in their length, so a
    /// long answer costs no more than reading it. Null when either is not in the wire form (<see cref="Parse"/>).
    /// </summary>
    public static int? CompareText(string a, string b)
    {
        if (!TryReadParts(a, out var aNegative, out var aInteger, out var aFraction)
            || !TryReadParts(b, out var bNegative, out var bInteger, out var bFraction))
        {
            return null;
        }

        if (aNegative != bNegative)
        {
            return aNegative ? -1 : 1;
        }

        // Without leading zeros, the longer integer part is the larger; a fraction that is a prefix of another is smaller.
        var magnitude = aInteger.Length.CompareTo(bInteger.Length);
        if (magnitude == 0)
        {
            magnitude = aInteger.SequenceCompareTo(bInteger);
        }

        if (magnitude == 0)
        {
            magnitude = aFraction.SequenceCompareTo(bFraction);
        }

        return aNegative ? -Math.Sign(magnitude) : Math.Sign(magnitude);
    }

    /// <summary>A value read from outside (a calculation's constant or an answer): the wire form within <see cref="MaxDigits"/>.</summary>
    public static ExactDecimal Operand(string text) =>
        InRange(Parse(text) ?? throw new LogicException(LogicErrors.NotANumber));

    /// <summary>The exact sum.</summary>
    public static ExactDecimal Add(ExactDecimal a, ExactDecimal b)
    {
        var s = Math.Max(a.S, b.S);
        return InRange(Normalise((a.N * Pow10(s - a.S)) + (b.N * Pow10(s - b.S)), s));
    }

    /// <summary>The exact difference.</summary>
    public static ExactDecimal Subtract(ExactDecimal a, ExactDecimal b) => Add(a, new ExactDecimal(-b.N, b.S));

    /// <summary>The exact product.</summary>
    public static ExactDecimal Multiply(ExactDecimal a, ExactDecimal b) => InRange(Normalise(a.N * b.N, a.S + b.S));

    /// <summary>The quotient, kept to <see cref="DivisionPlaces"/> places and rounded half away from zero.</summary>
    public static ExactDecimal Divide(ExactDecimal a, ExactDecimal b)
    {
        if (b.N.IsZero)
        {
            throw new LogicException(LogicErrors.DivisionByZero);
        }

        // a / b = (a.N * 10^b.S) / (b.N * 10^a.S), kept to DivisionPlaces places.
        var q = RoundedQuotient(a.N * Pow10(b.S + DivisionPlaces), b.N * Pow10(a.S));
        return InRange(Normalise(q, DivisionPlaces));
    }

    /// <summary>Rounds to <paramref name="scale"/> places, half away from zero.</summary>
    public static ExactDecimal Round(ExactDecimal a, int scale) =>
        a.S <= scale ? a : InRange(Normalise(RoundedQuotient(a.N, Pow10(a.S - scale)), scale));

    /// <summary>Applies the storage limits: at most 15 digits in all (leading zeros don't count), at most 15 after the point.</summary>
    public static ExactDecimal Stored(ExactDecimal d) =>
        Digits(d.N) > StoredDigits || d.S > StoredDigits ? throw new LogicException(LogicErrors.TooManyDigits) : d;

    /// <summary>The canonical wire form: no trailing fraction zeros, no "-0".</summary>
    public override string ToString()
    {
        var body = BigInteger.Abs(N).ToString(CultureInfo.InvariantCulture).PadLeft(S + 1, '0');
        var point = body.Length - S;
        var text = S == 0 ? body : body[..point] + "." + body[point..];
        return N.Sign < 0 ? "-" + text : text;
    }

    private static BigInteger Pow10(int k) => BigInteger.Pow(10, k);

    private static int Digits(BigInteger n) => BigInteger.Abs(n).ToString(CultureInfo.InvariantCulture).Length;

    private static ExactDecimal Normalise(BigInteger n, int s)
    {
        while (s > 0 && (n % 10).IsZero)
        {
            n /= 10;
            s--;
        }

        return new ExactDecimal(n, s);
    }

    private static ExactDecimal InRange(ExactDecimal d) =>
        Digits(d.N) > MaxDigits ? throw new LogicException(LogicErrors.OutOfRange) : d;

    /// <summary>num / den rounded to an integer, half away from zero (BigInteger division truncates toward zero).</summary>
    private static BigInteger RoundedQuotient(BigInteger num, BigInteger den)
    {
        var q = BigInteger.DivRem(num, den, out var r);
        if (2 * BigInteger.Abs(r) < BigInteger.Abs(den))
        {
            return q;
        }

        return (num.Sign < 0) != (den.Sign < 0) ? q - 1 : q + 1;
    }

    // The parts of a value in the wire form, as Parse reads it: the sign (never for zero, so -0 is 0), the integer
    // digits and the fraction's digits without their trailing zeros.
    private static bool TryReadParts(string text, out bool negative, out ReadOnlySpan<char> integer, out ReadOnlySpan<char> fraction)
    {
        negative = text.Length > 0 && text[0] == '-';
        var start = negative ? 1 : 0;
        var intEnd = ReadDigits(text, start);
        integer = text.AsSpan(start, intEnd - start);
        fraction = [];
        if (integer.Length == 0 || (integer.Length > 1 && integer[0] == '0'))
        {
            return false;
        }

        if (intEnd < text.Length)
        {
            var fractionEnd = ReadDigits(text, intEnd + 1);
            if (text[intEnd] != '.' || fractionEnd == intEnd + 1 || fractionEnd != text.Length)
            {
                return false;
            }

            fraction = text.AsSpan(intEnd + 1, fractionEnd - intEnd - 1).TrimEnd('0');
        }

        negative &= !(integer is "0" && fraction.IsEmpty);
        return true;
    }

    private static int ReadDigits(string text, int start)
    {
        var i = start;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        return i;
    }
}
