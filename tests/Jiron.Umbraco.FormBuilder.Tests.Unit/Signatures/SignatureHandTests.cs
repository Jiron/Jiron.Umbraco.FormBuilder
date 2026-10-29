using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Signatures;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Signatures;

/// <summary>
/// The package's own hand (ADR-0103): the letters, digits and signs names use, each inside its box, with its top where
/// a mark above sits; and the combining marks, each inside the place it sits.
/// </summary>
public sealed class SignatureHandTests
{
    [Test]
    public void TheHand_HasEveryBasicLatinLetterAndDigit()
    {
        var letters = SignatureHand.Letters.ToHashSet();

        Assert.That(
            Enumerable.Range('a', 26).Concat(Enumerable.Range('A', 26)).Concat(Enumerable.Range('0', 10)).Select(code => (char)code).Where(character => !letters.Contains(character)),
            Is.Empty);
    }

    [Test]
    public void EveryLetter_StaysInsideItsBox_WithItsTopWhereAMarkAboveSits()
    {
        Assert.Multiple(() =>
        {
            foreach (var character in SignatureHand.Letters)
            {
                var glyph = SignatureHand.GlyphOf(character)!;
                var points = Points(glyph.Path);
                var name = "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture);
                Assert.That(points, Is.Not.Empty, name);
                Assert.That(points.Select(point => point.X), Has.All.InRange(-60, glyph.Advance + 60), name + ": within its advance");
                Assert.That(points.Select(point => point.Y), Has.All.InRange(SignatureHand.Descender, SignatureHand.Ascender), name + ": between descender and ascender");
                if (!char.IsLetter(character))
                {
                    continue;
                }

                // A dotted letter's top is its stem's: a mark above takes the dot's place on its dotless form.
                var highest = character is 'i' or 'j' or (char)0x0133 ? SignatureHand.XHeight : points.Max(point => point.Y);
                Assert.That(glyph.Top, Is.InRange(highest - 60, highest + 1), name + ": its top at its highest point");
            }
        });
    }

    [Test]
    public void EveryMark_StaysInsideThePlaceItSits()
    {
        Assert.Multiple(() =>
        {
            foreach (var character in SignatureHand.MarkCharacters)
            {
                var mark = SignatureHand.MarkOf(character)!;
                var points = Points(mark.Path);
                var name = "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture) + " " + mark.Place;
                var (xs, ys) = mark.Place switch
                {
                    MarkPlace.Above => ((-130, 130), (60, 200)),
                    MarkPlace.Below => ((-100, 100), (-190, 0)),
                    MarkPlace.Ogonek => ((-80, 80), (-190, 0)),
                    _ => ((-20, 90), (-10, 110)),
                };
                Assert.That(points.Select(point => point.X), Has.All.InRange(xs.Item1, xs.Item2), name);
                Assert.That(points.Select(point => point.Y), Has.All.InRange(ys.Item1, ys.Item2), name);
                Assert.That(char.GetUnicodeCategory(character), Is.EqualTo(UnicodeCategory.NonSpacingMark), name + ": a combining mark");
            }
        });
    }

    [Test]
    public void ACharacterTheHandLacks_HasNoLetterOrMark()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SignatureHand.GlyphOf((char)0x0416), Is.Null, "a Cyrillic letter");
            Assert.That(SignatureHand.MarkOf('a'), Is.Null, "a letter is no mark");
            Assert.That(SignatureHand.MarkOf((char)0x0360), Is.Null, "a mark names do not use");
            Assert.That(SignatureHand.Box.Advance, Is.GreaterThan(0));
            Assert.That(SignatureHand.Space.Path, Is.Empty);
        });
    }

    // Every point a path names, in units.
    private static List<(int X, int Y)> Points(string path)
    {
        var points = new List<(int X, int Y)>();
        var tokens = path.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var at = 0; at < tokens.Length;)
        {
            var count = tokens[at++] switch
            {
                "M" or "L" => 1,
                "Q" => 2,
                "C" => 3,
                "Z" => 0,
                var other => throw new FormatException(other),
            };
            for (var point = 0; point < count; point++, at += 2)
            {
                points.Add((int.Parse(tokens[at], CultureInfo.InvariantCulture), int.Parse(tokens[at + 1], CultureInfo.InvariantCulture)));
            }
        }

        return points;
    }
}
