// Generated from Sparkline.csjs by CSJS. Do not edit.
// Gravicode Studios — led by Kang Fadhil.
import { csjsAverage } from './csjs-runtime.js';

/** Draws a sparkline into an SVG element. This is the kind of work Blazor Server is a poor fit for: it is pure client-side rendering that would otherwise cost a network round trip per interaction. Writing it in C# and transpiling means the component and the chart share one language. */
export class Sparkline {

  /** Renders values into the SVG with the given id. Called from Blazor through IJSRuntime. */
  static Render(elementId, values, stroke) {
    const svg = document.getElementById(elementId);
    if (svg === null || values.length < 2) {
      return;
    }
    const width = 100;
    const height = 30;
    const min = Math.min(...values);
    const max = Math.max(...values);
    let range = max - min;
    if (range === 0) {
      range = 1;
    }
    const points = [];
    for (let i = 0; i < values.length; i++) {
      const x = i / (values.length - 1) * width;
      const y = height - (values[i] - min) / range * height;
      points.push(`${x},${y}`);
    }
    const line = `<polyline fill="none" stroke="${stroke}" stroke-width="2" ` + `stroke-linecap="round" stroke-linejoin="round" points="${[...points].join(' ')}" />`;
    const area = `<polygon fill="${stroke}" opacity="0.12" ` + `points="0,${height} ${[...points].join(' ')} ${width},${height}" />`;
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.setAttribute('preserveAspectRatio', 'none');
    svg.innerHTML = area + line;
  }

  /** Returns summary statistics, so the component does not recompute them server-side. */
  static Summarise(values) {
    const result = new Object();
    if (values.length === 0) {
      return result;
    }
    (result['min'] = Math.min(...values));
    (result['max'] = Math.max(...values));
    (result['average'] = csjsAverage(values));
    (result['last'] = values.at(-1));
    (result['rising'] = values.at(-1) >= csjsAverage(values));
    return result;
  }
}
globalThis.Sparkline = Sparkline;
//# sourceMappingURL=Sparkline.js.map
