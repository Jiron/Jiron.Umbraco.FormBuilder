using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Signatures;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Signatures;

/// <summary>
/// A name typed instead of a signature, drawn on the server (ADR-0103): cleaned of what could disguise it, drawn in ink
/// on the pad's baseline, smaller and then wider for a long name, every Latin letter with its marks, a box for each
/// character the hand lacks, and the name kept exactly in the PNG.
/// </summary>
public sealed class TypedSignatureTests
{
    private const char Joiner = (char)0x200D;
    private const char NonJoiner = (char)0x200C;
    private const char Acute = (char)0x0301;

    private static IEnumerable<TestCaseData> Names()
    {
        yield return Case(null, null, "nothing typed");
        yield return Case(string.Empty, null, "empty");
        yield return Case("  " + (char)9 + " ", null, "white space alone");
        yield return Case("  Ada   Lovelace  ", "Ada Lovelace", "each run of white space one space, the ends trimmed");
        yield return Case("Ada" + (char)9 + "Lovelace" + (char)13 + (char)10, "Ada Lovelace", "tabs and line breaks");
        yield return Case((char)0xA0 + "Ada" + (char)0x2028 + (char)0x3000 + "Lovelace", "Ada Lovelace", "other spaces and separators");
        yield return Case("e" + Acute + "va", (char)0xE9 + "va", "composed (NFC)");
        yield return Case("Ada" + (char)7 + "Lo", "AdaLo", "a control character");
        yield return Case((char)0x202E + "Ada" + (char)0x202C, "Ada", "direction overrides, which can disguise a text");
        yield return Case("A" + (char)0x200B + "da" + (char)0xFEFF, "Ada", "invisible format characters");
        yield return Case("e" + (char)0x200B + Acute, ((char)0xE9).ToString(), "a mark composed once what stood between is gone");
        yield return Case("a" + Joiner + "b" + NonJoiner + "c", "a" + Joiner + "b" + NonJoiner + "c", "the joiners, which other scripts need");
        yield return Case(((char)0x200B).ToString(), null, "nothing left");
    }

    [TestCaseSource(nameof(Names))]
    public void ATypedName_IsCleanedOfWhatCouldDisguiseIt(string? typed, string? cleaned) =>
        Assert.That(TypedSignature.Clean(typed), Is.EqualTo(cleaned));

    [Test]
    public void AName_IsDrawnInInkOnThePadsBaseline_AndKeptExactlyInThePng()
    {
        var png = TypedSignature.Draw("Ada Lovelace", 900, 300);
        var (_, _, rgba) = PngPixels.Decode(png);
        var ink = PngPixels.Ink(png)!.Value;
        var own = TypedSignature.Draw("Ada", 400, 150);

        Assert.Multiple(() =>
        {
            Assert.That(PngHeader.Read(png), Is.EqualTo((900, 300)), "the default pad, a header the signature's check takes");
            Assert.That(TypedSignature.NameOf(png), Is.EqualTo("Ada Lovelace"));
            Assert.That(rgba.Chunk(4).Select(pixel => (pixel[0], pixel[1], pixel[2])).Distinct(), Is.EqualTo(new[] { ((byte)0x1F, (byte)0x24, (byte)0x30) }), "ink everywhere, its cover in the alpha");
            Assert.That(ink.Left, Is.InRange(50, 60), "from the margin");
            Assert.That(ink.Bottom, Is.InRange(225, 231), "on the baseline at three quarters of the height");
            Assert.That(ink.Top, Is.InRange(124, 134), "an ascender at 30% of the height for a cap");
            Assert.That(ink.Right, Is.LessThan(900 - 54));
            Assert.That(Stem("l"), Is.InRange(4, 7), "a pen of some five pixels at this size");
            Assert.That(PngHeader.Read(own), Is.EqualTo((400, 150)), "the field's own pad");
            Assert.That(PngPixels.Ink(own)!.Value.Bottom, Is.InRange(111, 116));
            Assert.That(TypedSignature.Draw("Ada Lovelace", 900, 300), Is.EqualTo(png), "the same name, the same file");
            Assert.That(TypedSignature.NameOf(PngPixels.Decode(png).Rgba), Is.Null, "no PNG, no name");
        });
    }

    [Test]
    public void ALongName_IsDrawnSmaller_ThenOnAWiderImage_ThenSmallerAgain()
    {
        var (brief, longer, widened, widest) = (Draw("AAA"), Draw(new string('A', 15)), Draw(new string('A', 40)), Draw(new string('A', 100)));

        Assert.Multiple(() =>
        {
            Assert.That(brief.Width, Is.EqualTo(900));
            Assert.That(brief.Height, Is.InRange(90, 98), "a cap at 30% of the height, and the pen");
            Assert.That(longer.Width, Is.EqualTo(900), "shrunk to fit");
            Assert.That(longer.Height, Is.InRange(52, 88));
            Assert.That(longer.Right, Is.InRange(800, 900 - 54), "up to the right margin");
            Assert.That(widened.Width, Is.InRange(901, 2699), "at 15% of the height the image widens");
            Assert.That(widened.Height, Is.InRange(45, 51));
            Assert.That(widest.Width, Is.EqualTo(2700), "as far as the signature's check allows");
            Assert.That(widest.Height, Is.LessThan(45), "and only then smaller again");
            Assert.That(widest.Right, Is.LessThan(2700));
        });

        static (int Width, int Height, int Right) Draw(string name)
        {
            var png = TypedSignature.Draw(name, 900, 300);
            var ink = PngPixels.Ink(png)!.Value;
            return (PngHeader.Read(png)!.Value.Width, ink.Bottom - ink.Top + 1, ink.Right);
        }
    }

    [Test]
    public void ALettersMarks_SitAboveOrBelowIt_StackingAboveIt()
    {
        var e = Top("e");
        var circumflex = Top("e" + (char)0x0302);
        var stacked = Top("e" + (char)0x0302 + (char)0x0303);

        Assert.Multiple(() =>
        {
            Assert.That(circumflex, Is.LessThan(e - 10), "a mark above");
            Assert.That(stacked, Is.LessThan(circumflex - 10), "a second mark above the first");
            Assert.That(Bottom("c" + (char)0x0327), Is.GreaterThan(Bottom("c") + 10), "a cedilla below");
            Assert.That(Bottom("a" + (char)0x0328), Is.GreaterThan(Bottom("a") + 10), "an ogonek at the foot");
            Assert.That(Bottom("s" + (char)0x0326), Is.GreaterThan(Bottom("s") + 10), "a comma below");
            Assert.That(LeftBelow("a" + (char)0x0328), Is.GreaterThan(LeftBelow("a" + (char)0x0327) + 10), "an ogonek at the foot's right, a cedilla in the middle");
            Assert.That(PngPixels.Alpha(Signed("o" + (char)0x031B)), Is.Not.EqualTo(PngPixels.Alpha(Signed("o"))), "a horn at the shoulder");
            Assert.That(Top("o" + (char)0x031B), Is.GreaterThan(Top("o" + (char)0x0302) + 5), "a horn below where a mark above sits");
            Assert.That(Top("E" + Acute), Is.LessThan(Top("E") - 10), "above a capital");
        });

        // The leftmost ink well below the baseline.
        static int LeftBelow(string name)
        {
            var (width, height, rgba) = PngPixels.Decode(TypedSignature.Draw(name, 900, 300));
            return Enumerable.Range(235, height - 235).SelectMany(y => Enumerable.Range(0, width).Where(x => rgba[(((y * width) + x) * 4) + 3] > 0)).Min();
        }

        // On the default pad, where a mark is some twenty pixels tall.
        static int Top(string name) => PngPixels.Ink(TypedSignature.Draw(name, 900, 300))!.Value.Top;

        static int Bottom(string name) => PngPixels.Ink(TypedSignature.Draw(name, 900, 300))!.Value.Bottom;
    }

    [Test]
    public void AnIOrAJ_LosesItsDotUnderAMarkAbove_AndKeepsItUnderAMarkBelow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Alpha((char)0xED + "j" + (char)0x030C), Is.EqualTo(Alpha((char)0x0131 + Acute.ToString() + (char)0x0237 + (char)0x030C)), "the dotless forms under an acute and a caron");
            Assert.That(Alpha("i" + (char)0x0323), Is.Not.EqualTo(Alpha((char)0x0131 + ((char)0x0323).ToString())), "a dot below keeps the dot above");
        });

        static byte[] Alpha(string name) => PngPixels.Alpha(Signed(name));
    }

    [Test]
    public void WhatTheHandLacks_IsABoxPerCharacter_AndTheNameIsKeptExactly()
    {
        var cyrillic = new string([(char)0x0415, (char)0x043B, (char)0x0435, (char)0x043D, (char)0x0430]);
        var han = new string([(char)0x65E5, (char)0x672C, (char)0x8A9E, (char)0x4E2D, (char)0x6587]);
        var smiling = new string([(char)0xD83D, (char)0xDE00]);
        var png = Signed(cyrillic);

        Assert.Multiple(() =>
        {
            Assert.That(PngPixels.Alpha(png), Is.EqualTo(PngPixels.Alpha(Signed(han))), "five boxes either way");
            Assert.That(TypedSignature.NameOf(png), Is.EqualTo(cyrillic), "the name kept as typed");
            Assert.That(PngPixels.Alpha(Signed("a" + (char)0x0360)), Is.EqualTo(PngPixels.Alpha(Signed(han[..1]))), "a letter with a mark the hand lacks: one box");
            Assert.That(PngPixels.Alpha(Signed(smiling)), Is.EqualTo(PngPixels.Alpha(Signed(han[..1]))), "a character of two code units: one box");
            Assert.That(PngPixels.Alpha(Signed(cyrillic[..1] + (char)0x0301)), Is.EqualTo(PngPixels.Alpha(Signed(han[..1]))), "a mark on a letter the hand lacks: one box");
        });
    }

    [Test]
    public void TheJoiners_AreKeptInTheName_ButNotDrawn()
    {
        var joined = "a" + Joiner + "b" + NonJoiner + "c";
        var png = Signed(joined);

        Assert.Multiple(() =>
        {
            Assert.That(PngPixels.Alpha(png), Is.EqualTo(PngPixels.Alpha(Signed("abc"))));
            Assert.That(TypedSignature.NameOf(png), Is.EqualTo(joined));
            Assert.That(PngPixels.Alpha(Signed("a b")), Is.Not.EqualTo(PngPixels.Alpha(Signed("ab"))), "a space is drawn as a space");
        });
    }

    [Test]
    public void EveryLatinLetterNamesUse_IsDrawn_NeverAsABox()
    {
        var box = PngPixels.Alpha(TypedSignature.Draw(((char)0x65E5).ToString(), 200, 100));
        var letters = Range(0x00C0, 0x017F).Concat(Range(0x01CD, 0x01DC)).Concat(Range(0x0218, 0x021B)).Concat(Range(0x1EA0, 0x1EF9)).Append((char)0x1E9E).Where(char.IsLetter);

        Assert.That(letters.Where(letter => PngPixels.Alpha(TypedSignature.Draw(letter.ToString(), 200, 100)).SequenceEqual(box)).Select(letter => "U+" + ((int)letter).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)), Is.Empty);

        static IEnumerable<char> Range(int from, int to) => Enumerable.Range(from, to - from + 1).Select(code => (char)code);
    }

    [Test]
    public void DrawingWithoutANameOrPad_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => TypedSignature.Draw(string.Empty, 900, 300), Throws.ArgumentException);
            Assert.That(() => TypedSignature.Draw(null!, 900, 300), Throws.ArgumentNullException);
            Assert.That(() => TypedSignature.Draw("Ada", 0, 300), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => TypedSignature.Draw("Ada", 900, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    // A name drawn on a small pad, fast to compare.
    private static byte[] Signed(string name) => TypedSignature.Draw(name, 300, 100);

    // How many pixels across a stem is well inked, halfway up the x-height on the default pad.
    private static int Stem(string name)
    {
        var (width, _, rgba) = PngPixels.Decode(TypedSignature.Draw(name, 900, 300));
        return Enumerable.Range(0, width).Count(x => rgba[(((193 * width) + x) * 4) + 3] >= 128);
    }

    private static TestCaseData Case(string? typed, string? cleaned, string name) => new TestCaseData(typed, cleaned).SetName("{m}: " + name);
}
