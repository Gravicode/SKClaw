using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Example of a custom plugin for SKClaw.
/// 
/// HOW TO USE:
/// 1. Add your skill class here (or create a new .cs file)
/// 2. Register it in your startup: kernel.ImportPluginFromObject(new MySkill(), "MySkill")
/// 3. Add "MySkill" to the Plugins:EnabledSkills list in app.config
/// 
/// The [KernelFunction] attribute makes a method available as an AI tool.
/// The [Description] attribute tells the LLM what the function does.
/// Parameters with [Description] are passed by the LLM when calling the tool.
/// </summary>
public class WeatherSkill
{
    private static readonly HttpClient _http = new();

    [KernelFunction, Description("Get the current weather for a city")]
    public async Task<string> GetWeatherAsync(
        [Description("City name, e.g. 'Jakarta' or 'New York'")] string city,
        [Description("Units: metric (Celsius) or imperial (Fahrenheit)")] string units = "metric")
    {
        // Using Open-Meteo (free, no API key required)
        // First, geocode the city
        var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1";
        var geoJson = await _http.GetStringAsync(geoUrl);
        var geo = System.Text.Json.JsonDocument.Parse(geoJson);

        if (!geo.RootElement.TryGetProperty("results", out var results) ||
            results.GetArrayLength() == 0)
            return $"City not found: {city}";

        var loc = results[0];
        var lat = loc.GetProperty("latitude").GetDouble();
        var lon = loc.GetProperty("longitude").GetDouble();
        var name = loc.GetProperty("name").GetString();
        var country = loc.TryGetProperty("country", out var c) ? c.GetString() : "";

        // Get weather
        var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
            $"&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m" +
            $"&temperature_unit={(units == "imperial" ? "fahrenheit" : "celsius")}";

        var weatherJson = await _http.GetStringAsync(weatherUrl);
        var weather = System.Text.Json.JsonDocument.Parse(weatherJson);
        var current = weather.RootElement.GetProperty("current");

        var temp = current.GetProperty("temperature_2m").GetDouble();
        var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
        var wind = current.GetProperty("wind_speed_10m").GetDouble();
        var code = current.GetProperty("weather_code").GetInt32();
        var unit = units == "imperial" ? "°F" : "°C";

        var condition = WeatherCodeToCondition(code);

        return $"Weather in {name}, {country}:\n" +
               $"🌡️ Temperature: {temp}{unit}\n" +
               $"💧 Humidity: {humidity}%\n" +
               $"💨 Wind: {wind} km/h\n" +
               $"☁️ Condition: {condition}";
    }

    private static string WeatherCodeToCondition(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Foggy",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Rain showers",
        95 => "Thunderstorm",
        _ => $"Unknown (code {code})"
    };
}

/// <summary>
/// Example: News skill using RSS feeds (no API key needed).
/// </summary>
public class NewsSkill
{
    private static readonly HttpClient _http = new();

    [KernelFunction, Description("Get latest news headlines")]
    public async Task<string> GetHeadlinesAsync(
        [Description("Topic or category: technology, business, science, health, sports, world")] string topic = "world",
        [Description("Number of headlines to return (1-10)")] int count = 5)
    {
        count = Math.Clamp(count, 1, 10);

        // Using Google News RSS (no API key)
        var url = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(topic)}&hl=en-US&gl=US&ceid=US:en";

        try
        {
            var xml = await _http.GetStringAsync(url);
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);

            var items = doc.SelectNodes("//item");
            if (items == null || items.Count == 0) return "No news found.";

            var headlines = new System.Text.StringBuilder();
            headlines.AppendLine($"Latest {topic} news:");

            int shown = 0;
            foreach (System.Xml.XmlNode item in items)
            {
                if (shown >= count) break;
                var title = item["title"]?.InnerText?.Trim();
                var pubDate = item["pubDate"]?.InnerText;
                if (!string.IsNullOrEmpty(title))
                {
                    headlines.AppendLine($"{shown + 1}. {title}");
                    if (!string.IsNullOrEmpty(pubDate))
                        headlines.AppendLine($"   [{pubDate}]");
                    shown++;
                }
            }

            return headlines.ToString();
        }
        catch (Exception ex)
        {
            return $"Could not fetch news: {ex.Message}";
        }
    }
}

/// <summary>
/// Example: Currency conversion skill (using frankfurter.app - free).
/// </summary>
public class CurrencySkill
{
    private static readonly HttpClient _http = new();

    [KernelFunction, Description("Convert currency amounts using current exchange rates")]
    public async Task<string> ConvertCurrencyAsync(
        [Description("Amount to convert")] double amount,
        [Description("Source currency code (e.g., USD, EUR, IDR)")] string fromCurrency,
        [Description("Target currency code (e.g., USD, EUR, IDR)")] string toCurrency)
    {
        try
        {
            var url = $"https://api.frankfurter.app/latest?amount={amount}&from={fromCurrency.ToUpper()}&to={toCurrency.ToUpper()}";
            var json = await _http.GetStringAsync(url);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            var rates = doc.RootElement.GetProperty("rates");
            if (!rates.TryGetProperty(toCurrency.ToUpper(), out var rate))
                return $"Currency {toCurrency} not found.";

            var result = rate.GetDouble();
            var date = doc.RootElement.GetProperty("date").GetString();

            return $"{amount:N2} {fromCurrency.ToUpper()} = {result:N2} {toCurrency.ToUpper()}\n(Rate as of {date})";
        }
        catch (Exception ex)
        {
            return $"Error converting currency: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get exchange rates for a base currency")]
    public async Task<string> GetRatesAsync(
        [Description("Base currency code")] string baseCurrency = "USD",
        [Description("Comma-separated list of target currencies, e.g. EUR,GBP,IDR")] string targetCurrencies = "EUR,GBP,JPY,IDR")
    {
        try
        {
            var targets = string.Join(",", targetCurrencies.Split(',').Select(c => c.Trim().ToUpper()));
            var url = $"https://api.frankfurter.app/latest?from={baseCurrency.ToUpper()}&to={targets}";
            var json = await _http.GetStringAsync(url);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            var rates = doc.RootElement.GetProperty("rates");
            var date = doc.RootElement.GetProperty("date").GetString();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Exchange rates for 1 {baseCurrency.ToUpper()} (as of {date}):");
            foreach (var prop in rates.EnumerateObject())
                sb.AppendLine($"  {prop.Name}: {prop.Value.GetDouble():N4}");

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}

/// <summary>
/// Example: Code review skill using AI.
/// </summary>
public class CodeReviewSkill
{
    private readonly Microsoft.SemanticKernel.Kernel _kernel;

    public CodeReviewSkill(Microsoft.SemanticKernel.Kernel kernel) => _kernel = kernel;

    [KernelFunction, Description("Review code for bugs, security issues, and improvements")]
    public async Task<string> ReviewCodeAsync(
        [Description("The code to review")] string code,
        [Description("Programming language (e.g., csharp, python, javascript)")] string language = "auto")
    {
        var prompt = $"""
            You are a senior software engineer. Review the following {(language == "auto" ? "" : language)} code and provide:
            1. 🐛 Bugs or errors found
            2. 🔒 Security concerns
            3. ⚡ Performance issues  
            4. 📝 Style/readability suggestions
            5. ✅ Summary and rating (1-10)

            Code:
            ```{language}
            {code}
            ```

            Be concise and actionable.
            """;

        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }

    [KernelFunction, Description("Generate unit tests for a given code")]
    public async Task<string> GenerateTestsAsync(
        [Description("The code to generate tests for")] string code,
        [Description("Testing framework (e.g., xunit, pytest, jest)")] string framework = "auto")
    {
        var prompt = $"""
            Generate comprehensive unit tests for the following code using {(framework == "auto" ? "an appropriate" : framework)} testing framework.
            Include edge cases, null checks, and typical usage scenarios.

            Code:
            {code}

            Return only the test code, ready to run.
            """;

        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "";
    }
}
