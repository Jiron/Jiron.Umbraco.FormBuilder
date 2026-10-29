using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Core.Signatures;

/// <summary>
/// A canvas a round pen draws on (ADR-0103): each stroke of a path, placed in pixels, drawn as its segments, each pixel
/// as covered as its distance from the nearest segment allows, so the edges are smooth and overlapping strokes do not
/// darken.
/// </summary>
/// <param name="width">The width in pixels.</param>
/// <param name="height">The height in pixels.</param>
internal sealed class PenCanvas(int width, int height)
{
    // How long a curve's piece may be, in pixels, before it is cut again.
    private const double LongestPiece = 2;

    // The most pieces a curve is cut into.
    private const int MostPieces = 64;

    private readonly float[] _cover = new float[width * height];

    /// <summary>The width in pixels.</summary>
    public int Width => width;

    /// <summary>The height in pixels.</summary>
    public int Height => height;

    /// <summary>Draws a path (<see cref="SignatureHand"/>'s commands) placed in pixels.</summary>
    /// <param name="path">The path.</param>
    /// <param name="place">Where a point in the path's units lands, in pixels.</param>
    /// <param name="pen">The pen's width in pixels.</param>
    public void Draw(string path, Func<double, double, (double X, double Y)> place, double pen)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(place);
        var tokens = path.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var radius = pen / 2;
        var at = 0;
        (double X, double Y) current = default, start = default;
        while (at < tokens.Length)
        {
            var command = tokens[at++];
            switch (command)
            {
                case "M":
                    current = start = Point();
                    break;
                case "L":
                    var to = Point();
                    Segment(current, to, radius);
                    current = to;
                    break;
                case "Q":
                    var control = Point();
                    var end = Point();
                    current = Curve(current, control, control, end, radius, quadratic: true);
                    break;
                case "C":
                    var first = Point();
                    var second = Point();
                    current = Curve(current, first, second, Point(), radius, quadratic: false);
                    break;
                case "Z":
                    Segment(current, start, radius);
                    current = start;
                    break;
                default:
                    throw new FormatException("Unknown path command: " + command);
            }
        }

        (double X, double Y) Point()
        {
            var x = int.Parse(tokens[at++], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var y = int.Parse(tokens[at++], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return place(x, y);
        }
    }

    /// <summary>The pixels in 8-bit red, green, blue and alpha, row by row: the ink's colour everywhere, as covered as drawn.</summary>
    /// <param name="red">The ink's red.</param>
    /// <param name="green">The ink's green.</param>
    /// <param name="blue">The ink's blue.</param>
    public byte[] Rgba(byte red, byte green, byte blue)
    {
        var pixels = new byte[_cover.Length * 4];
        for (var at = 0; at < _cover.Length; at++)
        {
            pixels[at * 4] = red;
            pixels[(at * 4) + 1] = green;
            pixels[(at * 4) + 2] = blue;
            pixels[(at * 4) + 3] = (byte)Math.Round(_cover[at] * 255, MidpointRounding.AwayFromZero);
        }

        return pixels;
    }

    // A curve (a quadratic's one control point given twice), cut into pieces short enough to look round; its end.
    private (double X, double Y) Curve((double X, double Y) from, (double X, double Y) first, (double X, double Y) second, (double X, double Y) to, double radius, bool quadratic)
    {
        var reach = Distance(from, first) + Distance(first, second) + Distance(second, to);
        var pieces = Math.Clamp((int)Math.Ceiling(reach / LongestPiece), 2, MostPieces);
        var previous = from;
        for (var piece = 1; piece <= pieces; piece++)
        {
            var t = (double)piece / pieces;
            var u = 1 - t;
            var next = quadratic
                ? ((u * u * from.X) + (2 * u * t * first.X) + (t * t * to.X), (u * u * from.Y) + (2 * u * t * first.Y) + (t * t * to.Y))
                : ((u * u * u * from.X) + (3 * u * u * t * first.X) + (3 * u * t * t * second.X) + (t * t * t * to.X),
                    (u * u * u * from.Y) + (3 * u * u * t * first.Y) + (3 * u * t * t * second.Y) + (t * t * t * to.Y));
            Segment(previous, next, radius);
            previous = next;
        }

        return to;
    }

    // One straight piece of a stroke: each pixel near it covered by how far its middle lies inside the pen's edge.
    private void Segment((double X, double Y) from, (double X, double Y) to, double radius)
    {
        var left = Math.Max(0, (int)Math.Floor(Math.Min(from.X, to.X) - radius - 1));
        var right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(from.X, to.X) + radius + 1));
        var top = Math.Max(0, (int)Math.Floor(Math.Min(from.Y, to.Y) - radius - 1));
        var bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(from.Y, to.Y) + radius + 1));
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var squared = (dx * dx) + (dy * dy);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;
                var t = squared == 0 ? 0 : Math.Clamp((((px - from.X) * dx) + ((py - from.Y) * dy)) / squared, 0, 1);
                var distance = Distance((px, py), (from.X + (t * dx), from.Y + (t * dy)));
                var cover = (float)Math.Clamp(radius + 0.5 - distance, 0, 1);
                if (cover > _cover[(y * width) + x])
                {
                    _cover[(y * width) + x] = cover;
                }
            }
        }
    }

    private static double Distance((double X, double Y) from, (double X, double Y) to) =>
        Math.Sqrt(((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));
}
