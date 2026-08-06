using System;
using System.Collections.Generic;
using System.Linq;
using CSJS;
using CSJS.Browser;

namespace CsjsBlazorApp.Client;

/// <summary>
/// Draws a sparkline into an SVG element.
/// <para>
/// This is the kind of work Blazor Server is a poor fit for: it is pure client-side rendering
/// that would otherwise cost a network round trip per interaction. Writing it in C# and
/// transpiling means the component and the chart share one language.
/// </para>
/// </summary>
[JsExport(Global = true)]
public static class Sparkline
{
    /// <summary>
    /// Renders values into the SVG with the given id. Called from Blazor through IJSRuntime.
    /// </summary>
    public static void Render(string elementId, double[] values, string stroke)
    {
        var svg = Document.GetElementById(elementId);
        if (svg is null || values.Length < 2) return;

        var width = 100.0;
        var height = 30.0;

        var min = values.Min();
        var max = values.Max();
        var range = max - min;

        // A flat series would divide by zero; centring it is the sensible reading.
        if (range == 0) range = 1;

        var points = new List<string>();
        for (var i = 0; i < values.Length; i++)
        {
            var x = i / (double)(values.Length - 1) * width;
            var y = height - (values[i] - min) / range * height;
            points.Add($"{x},{y}");
        }

        var line = $"<polyline fill=\"none\" stroke=\"{stroke}\" stroke-width=\"2\" " +
                   $"stroke-linecap=\"round\" stroke-linejoin=\"round\" points=\"{string.Join(" ", points)}\" />";

        // A translucent area under the line reads better than the stroke alone.
        var area = $"<polygon fill=\"{stroke}\" opacity=\"0.12\" " +
                   $"points=\"0,{height} {string.Join(" ", points)} {width},{height}\" />";

        svg.SetAttribute("viewBox", $"0 0 {width} {height}");
        svg.SetAttribute("preserveAspectRatio", "none");
        svg.InnerHtml = area + line;
    }

    /// <summary>Returns summary statistics, so the component does not recompute them server-side.</summary>
    public static JsObject Summarise(double[] values)
    {
        var result = new JsObject();
        if (values.Length == 0) return result;

        result.Set("min", values.Min());
        result.Set("max", values.Max());
        result.Set("average", values.Average());
        result.Set("last", values[^1]);
        // Trend compares the last value against the mean, which is enough for a sparkline label.
        result.Set("rising", values[^1] >= values.Average());
        return result;
    }
}
