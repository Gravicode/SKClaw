using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// GeoPlugin — Weather, geolocation, geocoding, timezone lookup,
/// distance calculation, maps, and address info.
/// Uses free APIs (Open-Meteo, OpenStreetMap Nominatim, Open-Elevation).
/// </summary>
public class GeoPlugin
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "SKClaw/1.0 (https://github.com/skclaw)" } }
    };

    // ── Weather ────────────────────────────────────────────────

    [KernelFunction, Description("Get current weather conditions for a city or location (free, no API key needed)")]
    public async Task<string> GetWeatherAsync(
        [Description("City name or location, e.g. 'Jakarta', 'New York', 'London'")] string location,
        [Description("Temperature unit: celsius or fahrenheit")] string unit = "celsius")
    {
        try
        {
            var (lat, lon, name, country) = await GeocodeAsync(location);
            var tempUnit = unit == "fahrenheit" ? "fahrenheit" : "celsius";
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                      $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m,uv_index,precipitation" +
                      $"&temperature_unit={tempUnit}&wind_speed_unit=kmh";

            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var cur = doc.RootElement.GetProperty("current");
            char u = unit == "fahrenheit" ? 'F' : 'C';
            var wCode = cur.GetProperty("weather_code").GetInt32();

            return $"""
                🌤️ Weather in {name}, {country}
                Condition   : {WmoCode(wCode)}
                Temperature : {cur.GetProperty("temperature_2m").GetDouble():F1}°{u}
                Feels Like  : {cur.GetProperty("apparent_temperature").GetDouble():F1}°{u}
                Humidity    : {cur.GetProperty("relative_humidity_2m").GetInt32()}%
                Wind        : {cur.GetProperty("wind_speed_10m").GetDouble():F0} km/h ({WindDir(cur.GetProperty("wind_direction_10m").GetInt32())})
                Precipitation: {cur.GetProperty("precipitation").GetDouble():F1} mm
                UV Index    : {cur.GetProperty("uv_index").GetDouble():F0}
                """;
        }
        catch (Exception ex) { return $"Weather error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get weather forecast for the next 7 days")]
    public async Task<string> GetForecastAsync(
        [Description("Location name")] string location,
        [Description("Days ahead (1-7)")] int days = 5,
        [Description("Temperature unit: celsius or fahrenheit")] string unit = "celsius")
    {
        try
        {
            days = Math.Clamp(days, 1, 7);
            var (lat, lon, name, country) = await GeocodeAsync(location);
            var tempUnit = unit == "fahrenheit" ? "fahrenheit" : "celsius";
            char u = unit == "fahrenheit" ? 'F' : 'C';
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                      $"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max" +
                      $"&temperature_unit={tempUnit}&wind_speed_unit=kmh&forecast_days={days}";

            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var daily = doc.RootElement.GetProperty("daily");

            var dates  = daily.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToList();
            var maxT   = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var minT   = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var precip = daily.GetProperty("precipitation_sum").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var codes  = daily.GetProperty("weather_code").EnumerateArray().Select(t => t.GetInt32()).ToList();
            var wind   = daily.GetProperty("wind_speed_10m_max").EnumerateArray().Select(t => t.GetDouble()).ToList();

            var sb = new StringBuilder($"📅 {days}-Day Forecast — {name}, {country}\n\n");
            for (int i = 0; i < dates.Count; i++)
            {
                var dt = DateTime.Parse(dates[i]).ToString("ddd MMM d");
                sb.AppendLine($"{dt,-12} {WmoCode(codes[i]),-20} {minT[i]:F0}–{maxT[i]:F0}°{u}  💧{precip[i]:F1}mm  💨{wind[i]:F0}km/h");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Forecast error: {ex.Message}"; }
    }

    // ── Geocoding ──────────────────────────────────────────────

    [KernelFunction, Description("Convert an address or location name to GPS coordinates (latitude, longitude)")]
    public async Task<string> GeocodeAddressAsync(
        [Description("Address or place name")] string address)
    {
        try
        {
            var (lat, lon, name, country) = await GeocodeAsync(address);
            return $"📍 {name}, {country}\nLatitude : {lat:F6}\nLongitude: {lon:F6}\nGoogle Maps: https://maps.google.com/?q={lat},{lon}";
        }
        catch (Exception ex) { return $"Geocoding failed: {ex.Message}"; }
    }

    [KernelFunction, Description("Convert GPS coordinates to a human-readable address (reverse geocoding)")]
    public async Task<string> ReverseGeocodeAsync(
        [Description("Latitude")] double latitude,
        [Description("Longitude")] double longitude)
    {
        try
        {
            var url = $"https://nominatim.openstreetmap.org/reverse?lat={latitude}&lon={longitude}&format=json";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var addr = doc.RootElement.GetProperty("address");
            var display = doc.RootElement.GetProperty("display_name").GetString();
            return $"📍 Coordinates: {latitude:F6}, {longitude:F6}\nAddress: {display}";
        }
        catch (Exception ex) { return $"Reverse geocoding failed: {ex.Message}"; }
    }

    [KernelFunction, Description("Calculate the distance between two locations")]
    public async Task<string> CalculateDistanceAsync(
        [Description("Starting location")] string from,
        [Description("Destination location")] string to,
        [Description("Unit: km or miles")] string unit = "km")
    {
        try
        {
            var (lat1, lon1, name1, _) = await GeocodeAsync(from);
            var (lat2, lon2, name2, _) = await GeocodeAsync(to);

            double distKm = HaversineKm(lat1, lon1, lat2, lon2);
            double result = unit.ToLower() == "miles" ? distKm * 0.621371 : distKm;
            string u = unit.ToLower() == "miles" ? "miles" : "km";

            return $"""
                📏 Distance Calculation
                From   : {name1} ({lat1:F4}, {lon1:F4})
                To     : {name2} ({lat2:F4}, {lon2:F4})
                Distance: {result:F2} {u} (straight line / as the crow flies)
                """;
        }
        catch (Exception ex) { return $"Distance error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get timezone information for a location")]
    public async Task<string> GetTimezoneForLocationAsync(
        [Description("Location name")] string location)
    {
        try
        {
            var (lat, lon, name, country) = await GeocodeAsync(location);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=&timezone=auto";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var tz     = doc.RootElement.GetProperty("timezone").GetString();
            var tzAbbr = doc.RootElement.GetProperty("timezone_abbreviation").GetString();
            var offset = doc.RootElement.GetProperty("utc_offset_seconds").GetInt32();

            return $"""
                🌐 Timezone for {name}, {country}
                Timezone : {tz}
                Abbr.    : {tzAbbr}
                UTC Offset: {offset/3600:+0;-0}:{Math.Abs(offset%3600/60):D2}
                """;
        }
        catch (Exception ex) { return $"Timezone error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get elevation (altitude above sea level) for GPS coordinates")]
    public async Task<string> GetElevationAsync(
        [Description("Latitude")] double latitude,
        [Description("Longitude")] double longitude)
    {
        try
        {
            var url = $"https://api.open-meteo.com/v1/elevation?latitude={latitude}&longitude={longitude}";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var elev = doc.RootElement.GetProperty("elevation")[0].GetDouble();
            return $"⛰️ Elevation at ({latitude:F4}, {longitude:F4}): {elev:F1} meters ({elev * 3.28084:F0} feet) above sea level";
        }
        catch (Exception ex) { return $"Elevation error: {ex.Message}"; }
    }

    [KernelFunction, Description("Search for places of interest near a location (restaurants, hospitals, etc.)")]
    public async Task<string> SearchNearbyPlacesAsync(
        [Description("Center location")] string location,
        [Description("Place type: restaurant, hospital, school, hotel, cafe, pharmacy, bank, supermarket")] string placeType,
        [Description("Radius in meters (max 5000)")] int radiusMeters = 1000,
        [Description("Max results")] int maxResults = 10)
    {
        try
        {
            var (lat, lon, name, _) = await GeocodeAsync(location);
            radiusMeters = Math.Clamp(radiusMeters, 100, 5000);
            maxResults   = Math.Clamp(maxResults, 1, 20);

            // Overpass API for OSM data
            var query = $"""[out:json][timeout:25];node["amenity"="{placeType}"](around:{radiusMeters},{lat},{lon});out {maxResults};""";
            var url = "https://overpass-api.de/api/interpreter";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent($"data={Uri.EscapeDataString(query)}")
            };
            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var elements = doc.RootElement.GetProperty("elements").EnumerateArray().ToList();

            if (elements.Count == 0) return $"No {placeType} found near {name} within {radiusMeters}m.";

            var sb = new StringBuilder($"📍 {placeType.ToUpper()} near {name} (within {radiusMeters}m):\n\n");
            foreach (var el in elements.Take(maxResults))
            {
                var elLat = el.GetProperty("lat").GetDouble();
                var elLon = el.GetProperty("lon").GetDouble();
                var dist = HaversineKm(lat, lon, elLat, elLon) * 1000;
                var tags = el.TryGetProperty("tags", out var t) ? t : default;
                var placeName = tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("name", out var n) ? n.GetString() : "Unnamed";
                sb.AppendLine($"  • {placeName} — {dist:F0}m away ({elLat:F4}, {elLon:F4})");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Places search error: {ex.Message}"; }
    }

    // ── Helpers ────────────────────────────────────────────────
    private async Task<(double lat, double lon, string name, string country)> GeocodeAsync(string query)
    {
        var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";
        var json = await _http.GetStringAsync(url);
        var results = JsonSerializer.Deserialize<JsonElement[]>(json);
        if (results == null || results.Length == 0) throw new Exception($"Location not found: {query}");
        var r = results[0];
        var lat     = double.Parse(r.GetProperty("lat").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var lon     = double.Parse(r.GetProperty("lon").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var display = r.GetProperty("display_name").GetString() ?? query;
        var parts   = display.Split(',');
        return (lat, lon, parts[0].Trim(), parts[^1].Trim());
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
    private static double ToRad(double d) => d * Math.PI / 180;

    private static string WindDir(int deg) => deg switch
    {
        < 22 or >= 338 => "N", < 67 => "NE", < 112 => "E", < 157 => "SE",
        < 202 => "S", < 247 => "SW", < 292 => "W", _ => "NW"
    };

    private static string WmoCode(int code) => code switch
    {
        0 => "☀️ Clear sky",
        1 => "🌤 Mostly clear",
        2 => "⛅ Partly cloudy",
        3 => "☁️ Overcast",
        45 or 48 => "🌫 Foggy",
        51 or 53 or 55 => "🌧 Drizzle",
        61 or 63 or 65 => "🌧 Rain",
        71 or 73 or 75 => "❄️ Snow",
        77 => "🌨 Snow grains",
        80 or 81 or 82 => "🌦 Rain showers",
        85 or 86 => "🌨 Snow showers",
        95 => "⛈️ Thunderstorm",
        96 or 99 => "⛈️ Thunderstorm + hail",
        _ => $"Code {code}"
    };
}
