using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.Signatures;

/// <summary>
/// The package's own hand for a typed signature (ADR-0103): each letter as pen strokes in units, the baseline at 0, the
/// x-height at 500, capitals at 700, ascenders at 740 and descenders at -230, upright (the drawing slants them). A path
/// is <c>M x y</c> to start a stroke, <c>L x y</c> for a line, <c>Q cx cy x y</c> and <c>C c1x c1y c2x c2y x y</c> for
/// curves, and <c>Z</c> to close one. A dot is a stroke that barely moves, which the round pen draws as a dot.
/// </summary>
internal static class SignatureHand
{
    /// <summary>The x-height, where a small letter's marks sit above.</summary>
    public const int XHeight = 500;

    /// <summary>The capitals' height.</summary>
    public const int CapHeight = 700;

    /// <summary>The ascenders' height.</summary>
    public const int Ascender = 740;

    /// <summary>The descenders' depth, below the baseline.</summary>
    public const int Descender = -230;

    /// <summary>The pen's width.</summary>
    public const int Pen = 42;

    /// <summary>How much higher each further mark above sits than the one before it.</summary>
    public const int MarkStep = 140;

    /// <summary>What stands for a character the hand has no letter for: an open box.</summary>
    public static readonly Glyph Box = new(460, "M 70 0 L 390 0 L 390 600 L 70 600 Z", 600);

    /// <summary>The space between words.</summary>
    public static readonly Glyph Space = new(280, string.Empty, 0);

    // The letters, digits and marks names use, by character; each with its advance and its top, where a mark above sits.
    private static readonly FrozenDictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>
    {
        ['a'] = new(500, "M 400 250 C 400 388 326 500 235 500 C 144 500 70 388 70 250 C 70 112 144 0 235 0 C 326 0 400 112 400 250 M 400 500 L 400 60 Q 400 0 455 0", XHeight),
        ['b'] = new(500, "M 80 740 L 80 0 M 80 250 C 80 388 160 500 255 500 C 350 500 430 388 430 250 C 430 112 350 0 255 0 C 160 0 80 112 80 250", Ascender),
        ['c'] = new(460, "M 400 410 C 370 470 315 500 250 500 C 151 500 70 388 70 250 C 70 112 151 0 250 0 C 315 0 370 30 400 90", XHeight),
        ['d'] = new(500, "M 400 250 C 400 388 326 500 235 500 C 144 500 70 388 70 250 C 70 112 144 0 235 0 C 326 0 400 112 400 250 M 400 740 L 400 60 Q 400 0 455 0", Ascender),
        ['e'] = new(490, "M 75 250 L 420 250 C 420 388 345 500 250 500 C 151 500 75 388 75 250 C 75 112 151 0 250 0 C 320 0 375 35 405 90", XHeight),
        ['f'] = new(330, "M 345 710 Q 310 740 260 740 Q 170 740 170 640 L 170 0 M 70 500 L 300 500", Ascender),
        ['g'] = new(500, "M 400 250 C 400 388 326 500 235 500 C 144 500 70 388 70 250 C 70 112 144 0 235 0 C 326 0 400 112 400 250 M 400 500 L 400 -80 C 400 -190 330 -230 240 -230 C 165 -230 115 -205 90 -165", XHeight),
        ['h'] = new(490, "M 80 740 L 80 0 M 80 330 C 110 440 180 500 260 500 C 360 500 410 440 410 340 L 410 0", Ascender),
        ['i'] = new(220, "M 110 500 L 110 0 M 110 640 L 112 652", XHeight),
        [(char)0x0131] = new(220, "M 110 500 L 110 0", XHeight),
        ['j'] = new(240, "M 160 500 L 160 -90 C 160 -190 110 -230 40 -230 C 0 -230 -30 -215 -45 -195 M 160 640 L 162 652", XHeight),
        [(char)0x0237] = new(240, "M 160 500 L 160 -90 C 160 -190 110 -230 40 -230 C 0 -230 -30 -215 -45 -195", XHeight),
        ['k'] = new(450, "M 80 740 L 80 0 M 380 500 L 80 200 M 190 310 L 410 0", Ascender),
        ['l'] = new(240, "M 100 740 L 100 70 Q 100 0 165 0", Ascender),
        ['m'] = new(640, "M 80 500 L 80 0 M 80 330 C 100 440 150 500 215 500 C 285 500 320 450 320 360 L 320 0 M 320 360 C 340 450 390 500 455 500 C 525 500 560 450 560 360 L 560 0", XHeight),
        ['n'] = new(490, "M 80 500 L 80 0 M 80 330 C 110 440 180 500 260 500 C 360 500 410 440 410 340 L 410 0", XHeight),
        ['o'] = new(500, "M 250 500 C 349 500 430 388 430 250 C 430 112 349 0 250 0 C 151 0 70 112 70 250 C 70 388 151 500 250 500", XHeight),
        ['p'] = new(500, "M 80 500 L 80 -230 M 80 250 C 80 388 160 500 255 500 C 350 500 430 388 430 250 C 430 112 350 0 255 0 C 160 0 80 112 80 250", XHeight),
        ['q'] = new(500, "M 400 250 C 400 388 326 500 235 500 C 144 500 70 388 70 250 C 70 112 144 0 235 0 C 326 0 400 112 400 250 M 400 500 L 400 -230", XHeight),
        ['r'] = new(380, "M 80 500 L 80 0 M 80 320 C 110 430 180 500 270 500 C 310 500 340 490 360 470", XHeight),
        ['s'] = new(460, "M 380 430 C 350 480 300 500 235 500 C 150 500 95 455 95 385 C 95 320 150 290 240 265 C 340 238 400 205 400 130 C 400 50 330 0 235 0 C 160 0 100 25 70 80", XHeight),
        ['t'] = new(330, "M 170 680 L 170 70 Q 170 0 240 0 Q 280 0 310 15 M 70 500 L 300 500", 680),
        ['u'] = new(490, "M 80 500 L 80 160 C 80 60 130 0 220 0 C 320 0 380 80 400 170 M 400 500 L 400 0", XHeight),
        ['v'] = new(460, "M 60 500 L 230 0 L 400 500", XHeight),
        ['w'] = new(620, "M 50 500 L 180 0 L 310 400 L 440 0 L 570 500", XHeight),
        ['x'] = new(470, "M 70 500 L 400 0 M 400 500 L 70 0", XHeight),
        ['y'] = new(470, "M 60 500 L 235 20 M 410 500 L 160 -195 Q 135 -230 85 -230", XHeight),
        ['z'] = new(470, "M 80 500 L 400 500 L 80 0 L 410 0", XHeight),

        ['A'] = new(580, "M 50 0 L 290 700 L 530 0 M 130 230 L 450 230", CapHeight),
        ['B'] = new(540, "M 90 0 L 90 700 L 300 700 C 390 700 440 650 440 560 C 440 470 380 400 290 400 L 90 400 M 290 400 C 410 400 470 330 470 210 C 470 80 400 0 290 0 L 90 0", CapHeight),
        ['C'] = new(580, "M 520 600 C 480 670 410 700 340 700 C 180 700 70 560 70 350 C 70 140 180 0 340 0 C 420 0 485 35 525 100", CapHeight),
        ['D'] = new(600, "M 90 0 L 90 700 L 260 700 C 440 700 530 560 530 350 C 530 140 440 0 260 0 L 90 0", CapHeight),
        ['E'] = new(520, "M 450 700 L 90 700 L 90 0 L 460 0 M 90 360 L 380 360", CapHeight),
        ['F'] = new(490, "M 450 700 L 90 700 L 90 0 M 90 370 L 380 370", CapHeight),
        ['G'] = new(610, "M 520 600 C 480 670 410 700 340 700 C 180 700 70 560 70 350 C 70 140 180 0 340 0 C 440 0 520 60 530 180 L 530 320 L 340 320", CapHeight),
        ['H'] = new(590, "M 90 0 L 90 700 M 500 0 L 500 700 M 90 360 L 500 360", CapHeight),
        ['I'] = new(220, "M 110 0 L 110 700 M 50 700 L 170 700 M 50 0 L 170 0", CapHeight),
        ['J'] = new(460, "M 380 700 L 380 180 C 380 60 310 0 210 0 C 120 0 60 50 40 130", CapHeight),
        ['K'] = new(540, "M 90 0 L 90 700 M 480 700 L 90 270 M 230 420 L 500 0", CapHeight),
        ['L'] = new(470, "M 90 700 L 90 0 L 430 0", CapHeight),
        ['M'] = new(660, "M 80 0 L 80 700 L 330 200 L 580 700 L 580 0", CapHeight),
        ['N'] = new(590, "M 90 0 L 90 700 L 500 0 L 500 700", CapHeight),
        ['O'] = new(660, "M 330 700 C 490 700 590 560 590 350 C 590 140 490 0 330 0 C 170 0 70 140 70 350 C 70 560 170 700 330 700", CapHeight),
        ['P'] = new(520, "M 90 0 L 90 700 L 300 700 C 410 700 470 630 470 530 C 470 430 410 360 300 360 L 90 360", CapHeight),
        ['Q'] = new(660, "M 330 700 C 490 700 590 560 590 350 C 590 140 490 0 330 0 C 170 0 70 140 70 350 C 70 560 170 700 330 700 M 380 170 L 580 -40", CapHeight),
        ['R'] = new(540, "M 90 0 L 90 700 L 300 700 C 410 700 470 630 470 530 C 470 430 410 360 300 360 L 90 360 M 280 360 L 490 0", CapHeight),
        ['S'] = new(560, "M 480 610 C 440 670 380 700 290 700 C 170 700 100 640 100 540 C 100 440 180 400 290 370 C 410 338 490 290 490 180 C 490 70 410 0 280 0 C 180 0 110 35 70 110", CapHeight),
        ['T'] = new(540, "M 40 700 L 500 700 M 270 700 L 270 0", CapHeight),
        ['U'] = new(600, "M 90 700 L 90 240 C 90 90 170 0 300 0 C 430 0 510 90 510 240 L 510 700", CapHeight),
        ['V'] = new(560, "M 40 700 L 280 0 L 520 700", CapHeight),
        ['W'] = new(760, "M 40 700 L 200 0 L 380 560 L 560 0 L 720 700", CapHeight),
        ['X'] = new(550, "M 60 700 L 490 0 M 490 700 L 60 0", CapHeight),
        ['Y'] = new(540, "M 40 700 L 270 340 L 500 700 M 270 340 L 270 0", CapHeight),
        ['Z'] = new(560, "M 70 700 L 480 700 L 70 0 L 490 0", CapHeight),

        ['0'] = new(540, "M 270 700 C 375 700 460 543 460 350 C 460 157 375 0 270 0 C 165 0 80 157 80 350 C 80 543 165 700 270 700", CapHeight),
        ['1'] = new(420, "M 90 560 L 260 700 L 260 0", CapHeight),
        ['2'] = new(540, "M 90 560 C 110 650 190 700 280 700 C 390 700 460 630 460 530 C 460 420 390 340 90 0 L 470 0", CapHeight),
        ['3'] = new(540, "M 100 620 C 140 680 200 700 270 700 C 380 700 440 640 440 550 C 440 460 370 400 260 400 M 260 400 C 390 400 460 330 460 210 C 460 80 380 0 260 0 C 180 0 110 30 80 100", CapHeight),
        ['4'] = new(540, "M 400 0 L 400 700 L 70 220 L 500 220", CapHeight),
        ['5'] = new(540, "M 450 700 L 140 700 L 110 400 C 160 430 210 440 270 440 C 390 440 460 360 460 230 C 460 90 380 0 260 0 C 180 0 110 30 80 100", CapHeight),
        ['6'] = new(540, "M 430 640 C 390 685 340 700 280 700 C 150 700 80 560 80 330 C 80 120 160 0 280 0 C 390 0 460 80 460 210 C 460 340 390 420 280 420 C 180 420 110 360 85 270", CapHeight),
        ['7'] = new(520, "M 70 700 L 470 700 L 200 0", CapHeight),
        ['8'] = new(540, "M 270 400 C 370 400 430 450 430 550 C 430 640 365 700 270 700 C 175 700 110 640 110 550 C 110 450 170 400 270 400 C 390 400 460 330 460 210 C 460 80 380 0 270 0 C 160 0 80 80 80 210 C 80 330 150 400 270 400", CapHeight),
        ['9'] = new(540, "M 110 60 C 150 15 200 0 260 0 C 390 0 460 140 460 370 C 460 580 380 700 260 700 C 150 700 80 620 80 490 C 80 360 150 280 260 280 C 360 280 430 340 455 430", CapHeight),

        ['-'] = new(350, "M 70 260 L 280 260", XHeight),
        [(char)0x2010] = new(350, "M 70 260 L 280 260", XHeight),
        [(char)0x2011] = new(350, "M 70 260 L 280 260", XHeight),
        ['\''] = new(200, "M 110 700 L 95 560", CapHeight),
        [(char)0x2019] = new(200, "M 125 700 Q 130 620 85 560", CapHeight),
        [(char)0x02BC] = new(200, "M 125 700 Q 130 620 85 560", CapHeight),
        [(char)0x2018] = new(200, "M 110 560 Q 70 620 110 700", CapHeight),
        ['.'] = new(200, "M 100 0 L 102 12", XHeight),
        [','] = new(200, "M 110 20 Q 110 -40 70 -110", XHeight),
        [(char)0x00B7] = new(200, "M 100 250 L 102 262", XHeight),
        ['&'] = new(560, "M 470 0 L 170 400 C 120 470 120 540 160 600 C 190 650 230 700 290 700 C 350 700 390 660 390 600 C 390 520 320 470 220 410 C 120 350 70 290 70 200 C 70 80 150 0 260 0 C 360 0 430 70 470 150 L 510 230", CapHeight),

        [(char)0x00DF] = new(510, "M 80 0 L 80 560 C 80 670 150 740 250 740 C 340 740 400 680 400 600 C 400 530 350 480 280 470 C 390 450 450 380 450 250 C 450 100 370 0 250 0 C 210 0 180 10 160 20", Ascender),
        [(char)0x1E9E] = new(560, "M 90 0 L 90 520 C 90 640 170 700 280 700 L 470 700 L 300 430 C 420 430 490 350 490 220 C 490 80 410 0 290 0 C 240 0 200 10 170 30", CapHeight),
        [(char)0x00E6] = new(700, "M 350 250 C 350 388 287 500 210 500 C 133 500 70 388 70 250 C 70 112 133 0 210 0 C 287 0 350 112 350 250 M 350 250 L 640 250 C 640 388 575 500 495 500 C 415 500 350 388 350 250 C 350 112 415 0 495 0 C 555 0 605 35 630 90", XHeight),
        [(char)0x00C6] = new(780, "M 40 0 L 390 700 L 720 700 M 390 700 L 390 0 L 730 0 M 390 360 L 680 360 M 160 240 L 390 240", CapHeight),
        [(char)0x00F8] = new(500, "M 250 500 C 349 500 430 388 430 250 C 430 112 349 0 250 0 C 151 0 70 112 70 250 C 70 388 151 500 250 500 M 60 -20 L 440 520", XHeight),
        [(char)0x00D8] = new(660, "M 330 700 C 490 700 590 560 590 350 C 590 140 490 0 330 0 C 170 0 70 140 70 350 C 70 560 170 700 330 700 M 70 -30 L 590 730", CapHeight),
        [(char)0x0153] = new(750, "M 230 500 C 140 500 70 388 70 250 C 70 112 140 0 230 0 C 320 0 390 112 390 250 C 390 388 320 500 230 500 M 390 250 L 690 250 C 690 388 620 500 540 500 C 460 500 390 388 390 250 C 390 112 460 0 540 0 C 600 0 650 35 680 90", XHeight),
        [(char)0x0152] = new(800, "M 720 700 L 380 700 L 380 0 L 730 0 M 380 360 L 660 360 M 380 700 L 330 700 C 170 700 70 560 70 350 C 70 140 170 0 330 0 L 380 0", CapHeight),
        [(char)0x00F0] = new(500, "M 250 500 C 349 500 430 388 430 250 C 430 112 349 0 250 0 C 151 0 70 112 70 250 C 70 388 151 500 250 500 M 430 250 C 430 470 350 650 190 740 M 190 600 L 400 700", Ascender),
        [(char)0x00D0] = new(600, "M 90 0 L 90 700 L 260 700 C 440 700 530 560 530 350 C 530 140 440 0 260 0 L 90 0 M 20 360 L 230 360", CapHeight),
        [(char)0x00FE] = new(500, "M 80 740 L 80 -230 M 80 250 C 80 388 160 500 255 500 C 350 500 430 388 430 250 C 430 112 350 0 255 0 C 160 0 80 112 80 250", Ascender),
        [(char)0x00DE] = new(510, "M 90 0 L 90 700 M 90 560 L 280 560 C 390 560 450 500 450 400 C 450 300 390 240 280 240 L 90 240", CapHeight),
        [(char)0x0142] = new(260, "M 110 740 L 110 70 Q 110 0 175 0 M 30 300 L 200 450", Ascender),
        [(char)0x0141] = new(470, "M 90 700 L 90 0 L 430 0 M 20 270 L 220 430", CapHeight),
        [(char)0x0111] = new(520, "M 400 250 C 400 388 326 500 235 500 C 144 500 70 388 70 250 C 70 112 144 0 235 0 C 326 0 400 112 400 250 M 400 740 L 400 60 Q 400 0 455 0 M 300 640 L 490 640", Ascender),
        [(char)0x0110] = new(600, "M 90 0 L 90 700 L 260 700 C 440 700 530 560 530 350 C 530 140 440 0 260 0 L 90 0 M 20 360 L 230 360", CapHeight),
        [(char)0x0127] = new(490, "M 80 740 L 80 0 M 80 330 C 110 440 180 500 260 500 C 360 500 410 440 410 340 L 410 0 M 20 640 L 200 640", Ascender),
        [(char)0x0126] = new(590, "M 90 0 L 90 700 M 500 0 L 500 700 M 90 360 L 500 360 M 30 540 L 560 540", CapHeight),
        [(char)0x014B] = new(490, "M 80 500 L 80 0 M 80 330 C 110 440 180 500 260 500 C 360 500 410 440 410 340 L 410 -100 C 410 -190 360 -230 290 -230 C 250 -230 220 -220 200 -205", XHeight),
        [(char)0x014A] = new(600, "M 90 0 L 90 700 M 90 520 C 160 650 250 700 340 700 C 450 700 510 620 510 480 L 510 0 C 510 -100 450 -150 370 -150", CapHeight),
        [(char)0x0133] = new(460, "M 110 500 L 110 0 M 110 640 L 112 652 M 380 500 L 380 -90 C 380 -190 330 -230 260 -230 C 220 -230 190 -215 175 -195 M 380 640 L 382 652", XHeight),
        [(char)0x0132] = new(680, "M 110 0 L 110 700 M 50 700 L 170 700 M 50 0 L 170 0 M 600 700 L 600 180 C 600 60 530 0 430 0 C 340 0 280 50 260 130", CapHeight),
        [(char)0x0138] = new(450, "M 80 500 L 80 0 M 380 500 L 80 200 M 190 310 L 410 0", XHeight),
        [(char)0x0140] = new(300, "M 100 740 L 100 70 Q 100 0 165 0 M 240 350 L 242 362", Ascender),
        [(char)0x013F] = new(470, "M 90 700 L 90 0 L 430 0 M 300 350 L 302 362", CapHeight),
        [(char)0x0149] = new(590, "M 95 700 L 80 560 M 180 500 L 180 0 M 180 330 C 210 440 280 500 360 500 C 460 500 510 440 510 340 L 510 0", CapHeight),
        [(char)0x0167] = new(330, "M 170 680 L 170 70 Q 170 0 240 0 Q 280 0 310 15 M 70 500 L 300 500 M 90 300 L 260 300", 680),
        [(char)0x0166] = new(540, "M 40 700 L 500 700 M 270 700 L 270 0 M 140 360 L 400 360", CapHeight),
        [(char)0x017F] = new(300, "M 345 710 Q 310 740 260 740 Q 170 740 170 640 L 170 0", Ascender),
    }.ToFrozenDictionary();

    // The combining marks names use: above a letter, below it, an ogonek at its foot, or a horn at its shoulder; each
    // drawn from its place: the top's middle above, the baseline's middle below, the foot's right for an ogonek, the
    // shoulder's right for a horn.
    private static readonly FrozenDictionary<char, Mark> Marks = new Dictionary<char, Mark>
    {
        [(char)0x0300] = new(MarkPlace.Above, "M 40 70 L -60 180"),
        [(char)0x0301] = new(MarkPlace.Above, "M -40 70 L 60 180"),
        [(char)0x0302] = new(MarkPlace.Above, "M -90 80 L 0 175 L 90 80"),
        [(char)0x0303] = new(MarkPlace.Above, "M -110 95 C -75 160 -35 160 0 125 C 35 90 75 90 110 155"),
        [(char)0x0304] = new(MarkPlace.Above, "M -100 125 L 100 125"),
        [(char)0x0306] = new(MarkPlace.Above, "M -90 175 C -80 105 -40 85 0 85 C 40 85 80 105 90 175"),
        [(char)0x0307] = new(MarkPlace.Above, "M 0 115 L 1 127"),
        [(char)0x0308] = new(MarkPlace.Above, "M -75 115 L -74 127 M 75 115 L 76 127"),
        [(char)0x0309] = new(MarkPlace.Above, "M -45 145 C -25 190 60 190 60 145 C 60 115 20 105 10 75"),
        [(char)0x030A] = new(MarkPlace.Above, "M 0 185 C 28 185 50 163 50 135 C 50 107 28 85 0 85 C -28 85 -50 107 -50 135 C -50 163 -28 185 0 185"),
        [(char)0x030B] = new(MarkPlace.Above, "M -85 70 L -5 180 M 35 70 L 115 180"),
        [(char)0x030C] = new(MarkPlace.Above, "M -90 175 L 0 80 L 90 175"),
        [(char)0x031B] = new(MarkPlace.Horn, "M -10 0 C 50 10 80 50 70 100"),
        [(char)0x0323] = new(MarkPlace.Below, "M 0 -115 L 1 -103"),
        [(char)0x0326] = new(MarkPlace.Below, "M 15 -50 Q 15 -110 -25 -170"),
        [(char)0x0327] = new(MarkPlace.Below, "M 0 0 L 0 -50 C 60 -60 90 -90 80 -130 C 70 -170 20 -180 -40 -165"),
        [(char)0x0328] = new(MarkPlace.Ogonek, "M 0 0 C -50 -40 -60 -110 -20 -150 C 0 -170 30 -170 50 -160"),
    }.ToFrozenDictionary();

    /// <summary>The letter, digit or sign the hand has for a character; null for any other.</summary>
    /// <param name="character">The character.</param>
    public static Glyph? GlyphOf(char character) => Glyphs.GetValueOrDefault(character);

    /// <summary>The combining mark the hand has for a character; null for any other.</summary>
    /// <param name="character">The combining character.</param>
    public static Mark? MarkOf(char character) => Marks.GetValueOrDefault(character);

    /// <summary>Every character the hand has a letter, digit or sign for.</summary>
    public static IEnumerable<char> Letters => Glyphs.Keys;

    /// <summary>Every combining mark the hand has.</summary>
    public static IEnumerable<char> MarkCharacters => Marks.Keys;
}

/// <summary>A letter, digit or sign of the hand (ADR-0103).</summary>
/// <param name="Advance">How far the next one starts, in units.</param>
/// <param name="Path">Its strokes.</param>
/// <param name="Top">The height a mark above sits on.</param>
internal sealed record Glyph(int Advance, string Path, int Top);

/// <summary>A combining mark of the hand (ADR-0103).</summary>
/// <param name="Place">Where it sits on its letter.</param>
/// <param name="Path">Its strokes, from its place.</param>
internal sealed record Mark(MarkPlace Place, string Path);

/// <summary>Where a combining mark sits on its letter.</summary>
internal enum MarkPlace
{
    /// <summary>Above the letter's top, in its middle; a further mark above sits higher.</summary>
    Above,

    /// <summary>Below the baseline, in the letter's middle.</summary>
    Below,

    /// <summary>At the foot of the letter's right side.</summary>
    Ogonek,

    /// <summary>At the letter's right shoulder.</summary>
    Horn,
}
