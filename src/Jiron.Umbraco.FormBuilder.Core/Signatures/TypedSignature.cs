using System.Globalization;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Signatures;

/// <summary>
/// A name typed instead of a signature, drawn as the signature's PNG on the server (ADR-0103): cleaned, written in the
/// package's own hand on the pad's size, and kept exactly in the PNG's <c>iTXt</c> chunk.
/// </summary>
internal static class TypedSignature
{
    /// <summary>The most UTF-16 code units a typed name may have, as its input's <c>maxlength</c> says.</summary>
    public const int LongestName = 100;

    /// <summary>The pad's width when the field sets none: the default theme's pad.</summary>
    public const int DefaultWidth = 900;

    /// <summary>The pad's height when the field sets none: the default theme's pad, at 3:1.</summary>
    public const int DefaultHeight = 300;

    /// <summary>The name the drawn signature is stored under.</summary>
    public const string FileName = "signature.png";

    /// <summary>The keyword of the chunk that holds the name as typed.</summary>
    public const string Keyword = "jfb:typed-name";

    /// <summary>How far a pad's width may grow for a long name: as far as the signature's check allows (ADR-0071).</summary>
    public const int MostWidening = 3;

    // The slant: tan 10 degrees.
    private const double Slant = 0.17632698070846498;

    // The margin left and right, of the pad's width; the baseline, of its height; the cap height, of its height, at
    // best and at least.
    private const double Margin = 0.06;
    private const double Baseline = 0.75;
    private const double LargestCaps = 0.30;
    private const double SmallestCaps = 0.15;

    // The narrowest pen, in pixels.
    private const double ThinnestPen = 1.5;

    // The joiners, which shape other scripts and are kept in the name, but which the hand draws without.
    private const char NonJoiner = (char)0x200C;
    private const char Joiner = (char)0x200D;

    // The ink: #1f2430.
    private const byte Red = 0x1F;
    private const byte Green = 0x24;
    private const byte Blue = 0x30;

    /// <summary>
    /// The name as it is drawn and stored: NFC, without control and format characters (but the joiners), each run of
    /// white space one space, trimmed; null when nothing is left.
    /// </summary>
    /// <param name="typed">The name as posted.</param>
    public static string? Clean(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var cleaned = new StringBuilder(typed.Length);
        var space = false;
        foreach (var rune in typed.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                space = cleaned.Length > 0;
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Control || (category == UnicodeCategory.Format && rune.Value is not (NonJoiner or Joiner)))
            {
                continue;
            }

            if (space)
            {
                cleaned.Append(' ');
                space = false;
            }

            cleaned.Append(rune.ToString());
        }

        return cleaned.Length == 0 ? null : cleaned.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The PNG of a cleaned name on a pad: transparent, in ink, on the baseline the pad draws its line on; a long name
    /// smaller, then on a wider image, then smaller again.
    /// </summary>
    /// <param name="name">The name, as <see cref="Clean"/> leaves it.</param>
    /// <param name="padWidth">The pad's width in pixels.</param>
    /// <param name="padHeight">The pad's height in pixels.</param>
    public static byte[] Draw(string name, int padWidth, int padHeight)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(padWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(padHeight, 1);
        var shapes = Shapes(name);
        var units = shapes.Sum(shape => shape.Glyph.Advance) + (SignatureHand.Ascender * Slant);
        var margin = Margin * padWidth;
        var scale = Math.Min(LargestCaps * padHeight / SignatureHand.CapHeight, (padWidth - (2 * margin)) / units);
        var width = padWidth;
        if (scale < SmallestCaps * padHeight / SignatureHand.CapHeight)
        {
            scale = SmallestCaps * padHeight / SignatureHand.CapHeight;
            width = (int)Math.Ceiling((units * scale) + (2 * margin));
            if (width > MostWidening * padWidth)
            {
                width = MostWidening * padWidth;
                scale = (width - (2 * margin)) / units;
            }
        }

        var canvas = new PenCanvas(width, padHeight);
        var pen = Math.Max(ThinnestPen, SignatureHand.Pen * scale);
        var baseline = Baseline * padHeight;
        var origin = margin;
        foreach (var (glyph, marks) in shapes)
        {
            var left = origin;
            canvas.Draw(glyph.Path, (x, y) => Place(left, x, y), pen);
            var above = 0;
            foreach (var mark in marks)
            {
                var (atX, atY) = mark.Place switch
                {
                    MarkPlace.Above => (glyph.Advance / 2, glyph.Top + (SignatureHand.MarkStep * above++)),
                    MarkPlace.Below => (glyph.Advance / 2, 0),
                    MarkPlace.Ogonek => (glyph.Advance - 90, 0),
                    _ => (glyph.Advance - 90, glyph.Top - 40),
                };
                canvas.Draw(mark.Path, (x, y) => Place(left, atX + x, atY + y), pen);
            }

            origin += glyph.Advance * scale;
        }

        return Png.Write(canvas.Width, canvas.Height, canvas.Rgba(Red, Green, Blue), Keyword, name);

        // A point in units of a letter starting at a place, slanted, in pixels.
        (double X, double Y) Place(double left, double x, double y) => (left + ((x + (y * Slant)) * scale), baseline - (y * scale));
    }

    /// <summary>The name a typed signature's PNG holds as typed (ADR-0103); null for any other file.</summary>
    /// <param name="png">The file's bytes.</param>
    public static string? NameOf(ReadOnlySpan<byte> png) => Png.Text(png, Keyword);

    // Each character the reader sees as one (a letter with its marks), as the hand draws it: its letter and marks, or a
    // box when the hand lacks either; a space as a space.
    private static List<(Glyph Glyph, IReadOnlyList<Mark> Marks)> Shapes(string name)
    {
        var shapes = new List<(Glyph, IReadOnlyList<Mark>)>();
        var elements = StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement().Replace(NonJoiner.ToString(), string.Empty, StringComparison.Ordinal).Replace(Joiner.ToString(), string.Empty, StringComparison.Ordinal);
            if (element.Length > 0)
            {
                shapes.Add(Shape(element));
            }
        }

        return shapes;
    }

    private static (Glyph Glyph, IReadOnlyList<Mark> Marks) Shape(string element)
    {
        if (element == " ")
        {
            return (SignatureHand.Space, []);
        }

        if (element.Length == 1 && SignatureHand.GlyphOf(element[0]) is { } whole)
        {
            return (whole, []);
        }

        var parts = element.Normalize(NormalizationForm.FormD);
        if (SignatureHand.GlyphOf(parts[0]) is not { } letter)
        {
            return (SignatureHand.Box, []);
        }

        var marks = new List<Mark>();
        foreach (var part in parts.AsSpan(1))
        {
            if (SignatureHand.MarkOf(part) is not { } mark)
            {
                return (SignatureHand.Box, []);
            }

            marks.Add(mark);
        }

        // An i or a j under a mark above loses its dot.
        var dotless = marks.Any(mark => mark.Place == MarkPlace.Above) ? parts[0] switch { 'i' => (char)0x0131, 'j' => (char)0x0237, _ => (char?)null } : null;
        return (dotless is { } plain ? SignatureHand.GlyphOf(plain)! : letter, marks);
    }
}
