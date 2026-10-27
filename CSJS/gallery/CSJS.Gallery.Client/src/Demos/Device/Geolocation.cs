using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Device;

/// <summary>
/// Where you are, and how far that is from everywhere else. The position comes from the
/// Geolocation API; the distances are great-circle distances computed in C# with the haversine
/// formula, and the map is a plain equirectangular projection drawn on a canvas.
/// </summary>
public sealed class Geolocation : Demo
{
    public override string Id => "geolocation";
    public override string Title => "Geolocation";
    public override string Category => "Device";
    public override string Summary => "Your position from navigator.geolocation, and the great-circle distance to fourteen cities, worked out in C# with the haversine formula and plotted on a projection drawn from scratch.";
    public override string[] Apis => new[] { "Geolocation", "GeolocationPosition", "Permissions", "Canvas 2D" };
    public override string Module => "Demos/Device/Geolocation";
    public override string Hint => "Share your location, or try a sample one.";

    private sealed record City(string Name, double Latitude, double Longitude);

    private static readonly City[] Cities =
    {
        new("Jakarta", -6.2088, 106.8456),
        new("Singapore", 1.3521, 103.8198),
        new("Tokyo", 35.6762, 139.6503),
        new("Sydney", -33.8688, 151.2093),
        new("Mumbai", 19.0760, 72.8777),
        new("Dubai", 25.2048, 55.2708),
        new("Nairobi", -1.2921, 36.8219),
        new("Cairo", 30.0444, 31.2357),
        new("London", 51.5074, -0.1278),
        new("Reykjavík", 64.1466, -21.9426),
        new("New York", 40.7128, -74.0060),
        new("San Francisco", 37.7749, -122.4194),
        new("Mexico City", 19.4326, -99.1332),
        new("São Paulo", -23.5505, -46.6333),
    };

    /// <summary>The mean radius of the Earth, in kilometres.</summary>
    private const double EarthRadius = 6371.0088;

    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private HTMLElement _list = null!;
    private HTMLElement _message = null!;
    private HTMLButtonElement _locate = null!;
    private HTMLElement _latitude = null!;
    private HTMLElement _longitude = null!;
    private HTMLElement _accuracy = null!;
    private HTMLElement _source = null!;
    private double? _watch;
    private (double Latitude, double Longitude)? _here;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("geo-stage");

        var map = stage.Add(Ui.El("div", "geo-map"));
        _canvas = map.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _ctx = _canvas.GetContext2D();
        _message = map.Add(Ui.El("div", "geo-message"));

        var controls = stage.Add(Ui.El("div", "stage-controls"));
        _locate = controls.Add(Ui.Button("Share my location", Locate, "btn btn-primary"));
        controls.Add(Ui.Button("Use a sample location", () => Show(-6.1754, 106.8272, 25, "Sample: Monas, Jakarta")));
        controls.Add(Ui.Readout("Latitude", out _latitude));
        controls.Add(Ui.Readout("Longitude", out _longitude));
        controls.Add(Ui.Readout("Accuracy", out _accuracy));
        controls.Add(Ui.Readout("Source", out _source));

        var distances = stage.Add(Ui.El("div", "geo-distances"));
        distances.Add(Ui.El("h3", "panel-title", "Great-circle distance"));
        _list = distances.Add(Ui.El("ol", "geo-list"));

        if (!Controls.Supports(Js.Navigator, "geolocation"))
        {
            _locate.Disabled = true;
            Message(Controls.Notice("This browser has no Geolocation API",
                "Use a sample location to see the distances, or open the gallery in a current browser.", "warn"));
        }
        else
        {
            Message(Controls.Notice("Where are you?",
                "Share your location and every city is measured from you. The position stays in this page; nothing is sent anywhere."));
            CheckPermission();
        }

        _resize = new ResizeObserver((entries, observer) =>
        {
            var ratio = Js.DevicePixelRatio;
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width * ratio);
            _canvas.Height = Math.Floor(box.Height * ratio);
            _ctx.SetTransform(ratio, 0, 0, ratio, 0, 0);
            Draw();
        });
        _resize.Observe(_canvas);
        RenderList();
    }

    public override void Unmount()
    {
        _resize?.Disconnect();
        if (_watch is double id) Js.Navigator.Geolocation.ClearWatch(id);
    }

    /// <summary>Says up front when permission was already refused, instead of failing on click.</summary>
    private async Task CheckPermission()
    {
        if (!Controls.Supports(Js.Navigator, "permissions")) return;
        try
        {
            var status = await Js.Navigator.Permissions.Query(new PermissionDescriptor { Name = "geolocation" });
            if (status.State == "denied") Denied();
        }
        catch (Exception)
        {
            // Some browsers do not answer for geolocation; asking on click still works.
        }
    }

    private void Locate()
    {
        _locate.Disabled = true;
        _locate.TextContent = "Locating…";
        Message(Controls.Notice("Finding you…", "The browser may ask for permission first. A fix can take a few seconds outdoors, longer indoors."));

        // watchPosition keeps refining: the first fix is often coarse, and later ones narrow it.
        _watch = Js.Navigator.Geolocation.WatchPosition(
            position => Show(position.Coords.Latitude, position.Coords.Longitude, position.Coords.Accuracy, "This device"),
            error =>
            {
                _locate.Disabled = false;
                _locate.TextContent = "Share my location";
                switch ((int)error.Code)
                {
                    case 1: Denied(); break;
                    case 2:
                        Message(Controls.Notice("Your position is unavailable",
                            "The device could not determine where it is. Check that location services are on, then try again.", "warn"));
                        break;
                    default:
                        Message(Controls.Notice("Finding you took too long",
                            "No fix arrived within fifteen seconds. Try again near a window, or use the sample location.", "warn"));
                        break;
                }
            },
            new PositionOptions { EnableHighAccuracy = true, Timeout = 15000, MaximumAge = 60000 });
    }

    private void Denied()
    {
        _locate.Disabled = true;
        Message(Controls.Notice("Location permission was refused",
            "Allow location for this site from the address bar's site settings and reload — or use the sample location to see the distances.", "warn"));
    }

    private void Show(double latitude, double longitude, double accuracy, string source)
    {
        _here = (latitude, longitude);
        _latitude.TextContent = $"{Math.Abs(latitude):F4}° {(latitude >= 0 ? "N" : "S")}";
        _longitude.TextContent = $"{Math.Abs(longitude):F4}° {(longitude >= 0 ? "E" : "W")}";
        _accuracy.TextContent = accuracy >= 1000 ? $"± {accuracy / 1000:F1} km" : $"± {accuracy:F0} m";
        _source.TextContent = source;
        _locate.Disabled = source != "This device" ? _locate.Disabled : false;
        if (source == "This device") _locate.TextContent = "Tracking";
        _message.InnerHTML = "";
        _message.ClassList.Add("is-hidden");
        RenderList();
        Draw();
    }

    private void Message(HTMLElement notice)
    {
        _message.InnerHTML = "";
        _message.ClassList.Remove("is-hidden");
        _message.Add(notice);
    }

    /// <summary>
    /// The haversine formula: the central angle between two points from their latitudes and
    /// longitudes, stable even for points close together.
    /// </summary>
    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        var φ1 = lat1 * Math.PI / 180;
        var φ2 = lat2 * Math.PI / 180;
        var Δφ = (lat2 - lat1) * Math.PI / 180;
        var Δλ = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2)
                + Math.Cos(φ1) * Math.Cos(φ2) * Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
        return 2 * EarthRadius * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>The initial compass bearing from one point toward another, as N, NE, E…</summary>
    private static string Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var φ1 = lat1 * Math.PI / 180;
        var φ2 = lat2 * Math.PI / 180;
        var Δλ = (lon2 - lon1) * Math.PI / 180;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        var degrees = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return points[(int)Math.Round(degrees / 45) % 8];
    }

    private void RenderList()
    {
        _list.InnerHTML = "";
        if (_here is not { } here)
        {
            foreach (var city in Cities.Take(6))
            {
                var row = _list.Add(Ui.El("li", "geo-row is-waiting"));
                row.Add(Ui.El("span", "geo-city", city.Name));
                row.Add(Ui.El("span", "geo-bar"));
                row.Add(Ui.El("span", "geo-km", "— km"));
            }
            return;
        }

        // Half the circumference is as far as anywhere can be.
        var farthest = Math.PI * EarthRadius;
        var ranked = Cities
            .Select(c => (City: c, Km: Distance(here.Latitude, here.Longitude, c.Latitude, c.Longitude)))
            .OrderBy(x => x.Km)
            .ToList();

        foreach (var (city, km) in ranked)
        {
            var row = _list.Add(Ui.El("li", "geo-row"));
            row.Add(Ui.El("span", "geo-city", city.Name));
            var bar = row.Add(Ui.El("span", "geo-bar"));
            var fill = bar.Add(Ui.El("i"));
            fill.Style.Width = $"{Math.Max(1.5, km / farthest * 100):F1}%";
            row.Add(Ui.El("span", "geo-km", $"{km:N0} km {Bearing(here.Latitude, here.Longitude, city.Latitude, city.Longitude)}"));
        }
    }

    private double X(double longitude, double width) => (longitude + 180) / 360 * width;
    private double Y(double latitude, double height) => (90 - latitude) / 180 * height;

    /// <summary>A graticule, the cities, and lines from you to each of them.</summary>
    private void Draw()
    {
        var box = _canvas.GetBoundingClientRect();
        var w = box.Width;
        var h = box.Height;
        _ctx.FillStyle = "#0d0b14";
        _ctx.FillRect(0, 0, w, h);

        // Every 15°, with the equator and prime meridian a little brighter.
        _ctx.LineWidth = 1;
        for (var lon = -180; lon <= 180; lon += 15)
        {
            _ctx.StrokeStyle = lon == 0 ? "rgba(167, 139, 250, 0.28)" : "rgba(167, 139, 250, 0.08)";
            _ctx.BeginPath();
            _ctx.MoveTo(Math.Round(X(lon, w)) + 0.5, 0);
            _ctx.LineTo(Math.Round(X(lon, w)) + 0.5, h);
            _ctx.Stroke();
        }
        for (var lat = -75; lat <= 75; lat += 15)
        {
            _ctx.StrokeStyle = lat == 0 ? "rgba(167, 139, 250, 0.28)" : "rgba(167, 139, 250, 0.08)";
            _ctx.BeginPath();
            _ctx.MoveTo(0, Math.Round(Y(lat, h)) + 0.5);
            _ctx.LineTo(w, Math.Round(Y(lat, h)) + 0.5);
            _ctx.Stroke();
        }

        _ctx.Font = "10px 'JetBrains Mono', monospace";
        _ctx.FillStyle = "rgba(163, 157, 189, 0.5)";
        _ctx.FillText("0°", X(0, w) + 4, 14);
        _ctx.FillText("equator", 6, Y(0, h) - 5);

        if (_here is { } here)
        {
            var hx = X(here.Longitude, w);
            var hy = Y(here.Latitude, h);
            _ctx.StrokeStyle = "rgba(251, 191, 36, 0.32)";
            _ctx.SetLineDash(new[] { 3.0, 4 });
            foreach (var city in Cities)
            {
                _ctx.BeginPath();
                _ctx.MoveTo(hx, hy);
                _ctx.LineTo(X(city.Longitude, w), Y(city.Latitude, h));
                _ctx.Stroke();
            }
            _ctx.SetLineDash(Array.Empty<double>());
        }

        foreach (var city in Cities)
        {
            var x = X(city.Longitude, w);
            var y = Y(city.Latitude, h);
            _ctx.FillStyle = "#a78bfa";
            _ctx.BeginPath();
            _ctx.Arc(x, y, 3.5, 0, Math.PI * 2);
            _ctx.Fill();
            _ctx.FillStyle = "rgba(238, 234, 248, 0.75)";
            _ctx.Font = "11px 'Instrument Sans', sans-serif";
            _ctx.FillText(city.Name, x + 7, y + 4);
        }

        if (_here is { } you)
        {
            var x = X(you.Longitude, w);
            var y = Y(you.Latitude, h);
            _ctx.FillStyle = "rgba(251, 191, 36, 0.18)";
            _ctx.BeginPath();
            _ctx.Arc(x, y, 16, 0, Math.PI * 2);
            _ctx.Fill();
            _ctx.FillStyle = "#fbbf24";
            _ctx.BeginPath();
            _ctx.Arc(x, y, 5.5, 0, Math.PI * 2);
            _ctx.Fill();
            _ctx.Font = "600 11px 'Martian Mono', monospace";
            // Below and to the left, where city labels (always drawn to the right) are not.
            _ctx.TextAlign = "right";
            _ctx.FillText("YOU", x - 12, y + 18);
            _ctx.TextAlign = "left";
        }
    }
}
