using System.Globalization;
using System.Text;

namespace PortfolioManager.Api.Public;

/// <summary>The points of a small trend line drawn as an SVG polyline, so the chart needs no JavaScript.</summary>
public static class Sparkline
{
    public const int Width = 80;
    public const int Height = 22;

    /// <summary>"x,y x,y ..." scaled to fill the box (y grows downwards); empty for fewer than two values.</summary>
    public static string Points(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
            return "";
        var min = values.Min();
        var range = values.Max() - min;
        var points = new StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            var x = (decimal)Width * i / (values.Count - 1);
            // A flat line sits in the middle; otherwise leave 2px top and bottom for the stroke.
            var y = range == 0 ? Height / 2m : 2 + (Height - 4) * (1 - (values[i] - min) / range);
            points.Append(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#} ");
        }
        return points.ToString().TrimEnd();
    }
}
