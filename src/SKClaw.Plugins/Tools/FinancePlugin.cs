using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// FinancePlugin — Real-time stock prices, crypto, forex, financial metrics,
/// and portfolio calculations using free public APIs.
/// </summary>
public class FinancePlugin
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ── Stock Market ───────────────────────────────────────────

    [KernelFunction, Description("Get current stock price and basic info for a ticker symbol (uses Yahoo Finance)")]
    public async Task<string> GetStockPriceAsync(
        [Description("Stock ticker symbol, e.g. AAPL, GOOGL, TSLA, BBCA.JK")] string ticker)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(ticker)}?range=1d&interval=1m";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = result.GetProperty("meta");

            var price    = meta.GetProperty("regularMarketPrice").GetDouble();
            var prev     = meta.GetProperty("previousClose").GetDouble();
            var change   = price - prev;
            var changePct= change / prev * 100;
            var currency = meta.TryGetProperty("currency", out var c) ? c.GetString() : "USD";
            var name     = meta.TryGetProperty("longName", out var ln) ? ln.GetString() : ticker;
            var exchange = meta.TryGetProperty("exchangeName", out var ex) ? ex.GetString() : "";

            return $"""
                📊 {name} ({ticker.ToUpper()}) — {exchange}
                Price    : {price:N2} {currency}
                Change   : {change:+0.00;-0.00} ({changePct:+0.00;-0.00}%)
                Prev Close: {prev:N2}
                """;
        }
        catch (Exception ex) { return $"Could not fetch {ticker}: {ex.Message}"; }
    }

    [KernelFunction, Description("Get historical stock data for a ticker (daily close prices)")]
    public async Task<string> GetStockHistoryAsync(
        [Description("Ticker symbol")] string ticker,
        [Description("Period: 1mo, 3mo, 6mo, 1y, 2y, 5y")] string period = "1mo")
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(ticker)}?range={period}&interval=1d";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var timestamps = result.GetProperty("timestamp").EnumerateArray().Select(t => DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).ToString("yyyy-MM-dd")).ToList();
            var closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? (double?)null : v.GetDouble()).ToList();

            var sb = new StringBuilder($"📈 {ticker.ToUpper()} History ({period}):\n");
            var pairs = timestamps.Zip(closes).Where(p => p.Second.HasValue).TakeLast(20);
            foreach (var (date, price) in pairs)
                sb.AppendLine($"  {date}: {price:N2}");

            if (closes.Any(v => v.HasValue))
            {
                var valid = closes.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                var hi = valid.Max(); var lo = valid.Min();
                var first = valid.First(); var last = valid.Last();
                sb.AppendLine($"\nPeriod High: {hi:N2}  Low: {lo:N2}");
                sb.AppendLine($"Period Return: {(last - first) / first * 100:+0.00;-0.00}%");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get multiple stock quotes at once")]
    public async Task<string> GetMultipleStocksAsync(
        [Description("Comma-separated ticker symbols, e.g. AAPL,MSFT,GOOGL")] string tickers)
    {
        var symbols = tickers.Split(',').Select(t => t.Trim().ToUpper()).ToList();
        var sb = new StringBuilder("📊 Stock Quotes:\n\n");
        foreach (var sym in symbols.Take(10))
        {
            var result = await GetStockPriceAsync(sym);
            sb.AppendLine(result);
        }
        return sb.ToString().TrimEnd();
    }

    // ── Cryptocurrency ─────────────────────────────────────────

    [KernelFunction, Description("Get current cryptocurrency price and market data (uses CoinGecko free API)")]
    public async Task<string> GetCryptoPriceAsync(
        [Description("Coin ID, e.g. bitcoin, ethereum, solana, binancecoin")] string coinId,
        [Description("Currency: usd, eur, idr, btc")] string currency = "usd")
    {
        try
        {
            var url = $"https://api.coingecko.com/api/v3/coins/{coinId}?localization=false&tickers=false&market_data=true&community_data=false&developer_data=false";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement;
            var md = data.GetProperty("market_data");

            var price   = md.GetProperty("current_price").GetProperty(currency).GetDouble();
            var high24  = md.GetProperty("high_24h").GetProperty(currency).GetDouble();
            var low24   = md.GetProperty("low_24h").GetProperty(currency).GetDouble();
            var chg24   = md.GetProperty("price_change_percentage_24h").GetDouble();
            var chg7d   = md.GetProperty("price_change_percentage_7d").GetDouble();
            var mcap    = md.GetProperty("market_cap").GetProperty(currency).GetDouble();
            var vol24   = md.GetProperty("total_volume").GetProperty(currency).GetDouble();
            var rank    = data.GetProperty("market_cap_rank").GetInt32();
            var name    = data.GetProperty("name").GetString();
            var symbol  = data.GetProperty("symbol").GetString()!.ToUpper();

            return $"""
                🪙 {name} ({symbol}) — Rank #{rank}
                Price    : {price:N8} {currency.ToUpper()}
                24h High : {high24:N8}
                24h Low  : {low24:N8}
                24h Chg  : {chg24:+0.00;-0.00}%
                7d Chg   : {chg7d:+0.00;-0.00}%
                Mkt Cap  : {mcap:N0} {currency.ToUpper()}
                Volume   : {vol24:N0} {currency.ToUpper()}
                """;
        }
        catch (Exception ex) { return $"Error fetching {coinId}: {ex.Message}"; }
    }

    [KernelFunction, Description("Get top N cryptocurrencies by market cap")]
    public async Task<string> GetTopCryptosAsync(
        [Description("Number of coins to return (1-25)")] int count = 10,
        [Description("Currency for prices")] string currency = "usd")
    {
        count = Math.Clamp(count, 1, 25);
        try
        {
            var url = $"https://api.coingecko.com/api/v3/coins/markets?vs_currency={currency}&order=market_cap_desc&per_page={count}&page=1";
            var json = await _http.GetStringAsync(url);
            var coins = JsonSerializer.Deserialize<JsonElement[]>(json);
            var sb = new StringBuilder($"🏆 Top {count} Cryptocurrencies by Market Cap:\n\n");
            foreach (var (coin, i) in (coins ?? []).Select((c, i) => (c, i)))
            {
                var name  = coin.GetProperty("name").GetString();
                var sym   = coin.GetProperty("symbol").GetString()!.ToUpper();
                var price = coin.GetProperty("current_price").GetDouble();
                var chg   = coin.GetProperty("price_change_percentage_24h").GetDouble();
                sb.AppendLine($"{i+1,2}. {name,-18} ({sym,-6}) {price,14:N4} {currency.ToUpper()} | 24h: {chg:+0.00;-0.00}%");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── Forex ──────────────────────────────────────────────────

    [KernelFunction, Description("Get current exchange rate between two currencies (uses Frankfurter API, free)")]
    public async Task<string> GetForexRateAsync(
        [Description("Base currency code, e.g. USD, EUR, IDR")] string from,
        [Description("Target currency code")] string to,
        [Description("Amount to convert")] double amount = 1)
    {
        try
        {
            var url = $"https://api.frankfurter.app/latest?amount={amount}&from={from.ToUpper()}&to={to.ToUpper()}";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var rate = doc.RootElement.GetProperty("rates").GetProperty(to.ToUpper()).GetDouble();
            var date = doc.RootElement.GetProperty("date").GetString();
            return $"💱 {amount} {from.ToUpper()} = {rate:N6} {to.ToUpper()} (as of {date})";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Get exchange rates for multiple currencies from a base currency")]
    public async Task<string> GetForexTableAsync(
        [Description("Base currency")] string baseCurrency = "USD",
        [Description("Comma-separated target currencies, e.g. EUR,GBP,JPY,IDR,SGD")] string targets = "EUR,GBP,JPY,IDR,SGD,AUD,CAD")
    {
        try
        {
            var t = string.Join(",", targets.Split(',').Select(x => x.Trim().ToUpper()));
            var url = $"https://api.frankfurter.app/latest?from={baseCurrency.ToUpper()}&to={t}";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var date = doc.RootElement.GetProperty("date").GetString();
            var sb = new StringBuilder($"💱 Exchange Rates — 1 {baseCurrency.ToUpper()} (as of {date}):\n\n");
            foreach (var rate in doc.RootElement.GetProperty("rates").EnumerateObject())
                sb.AppendLine($"  {rate.Name,-5} : {rate.Value.GetDouble(),12:N4}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── Financial Calculations ─────────────────────────────────

    [KernelFunction, Description("Calculate Dollar-Cost Averaging (DCA) investment returns")]
    public string CalculateDcaAsync(
        [Description("Periodic investment amount")] double amount,
        [Description("Investment frequency: weekly, monthly, annually")] string frequency,
        [Description("Annual expected return percentage")] double annualReturnPct,
        [Description("Investment period in years")] double years)
    {
        double periodsPerYear = frequency.ToLower() switch { "weekly" => 52, "monthly" => 12, _ => 1 };
        double n = years * periodsPerYear;
        double r = annualReturnPct / 100.0 / periodsPerYear;
        double fv = r == 0 ? amount * n : amount * (Math.Pow(1 + r, n) - 1) / r * (1 + r);
        double totalInvested = amount * n;
        double profit = fv - totalInvested;

        return $"""
            📈 DCA Calculator
            Investment    : {amount:N2} per {frequency}
            Period        : {years} years ({(int)n} payments)
            Expected Rate : {annualReturnPct}% p.a.
            Total Invested: {totalInvested:N2}
            Final Value   : {fv:N2}
            Total Profit  : {profit:N2} ({profit/totalInvested*100:F1}% gain)
            """;
    }

    [KernelFunction, Description("Calculate options pricing using Black-Scholes model")]
    public string BlackScholes(
        [Description("Current stock price (S)")] double stockPrice,
        [Description("Option strike price (K)")] double strikePrice,
        [Description("Time to expiration in years (T)")] double timeToExpiry,
        [Description("Risk-free rate as decimal, e.g. 0.05 for 5%")] double riskFreeRate,
        [Description("Annual volatility as decimal, e.g. 0.2 for 20%")] double volatility,
        [Description("Option type: call or put")] string optionType = "call")
    {
        double d1 = (Math.Log(stockPrice / strikePrice) + (riskFreeRate + 0.5 * volatility * volatility) * timeToExpiry) /
                    (volatility * Math.Sqrt(timeToExpiry));
        double d2 = d1 - volatility * Math.Sqrt(timeToExpiry);

        double price = optionType.ToLower() == "call"
            ? stockPrice * N(d1) - strikePrice * Math.Exp(-riskFreeRate * timeToExpiry) * N(d2)
            : strikePrice * Math.Exp(-riskFreeRate * timeToExpiry) * N(-d2) - stockPrice * N(-d1);

        double delta = optionType.ToLower() == "call" ? N(d1) : N(d1) - 1;
        double gamma = Phi(d1) / (stockPrice * volatility * Math.Sqrt(timeToExpiry));
        double theta = -(stockPrice * Phi(d1) * volatility) / (2 * Math.Sqrt(timeToExpiry)) -
                       riskFreeRate * strikePrice * Math.Exp(-riskFreeRate * timeToExpiry) *
                       (optionType.ToLower() == "call" ? N(d2) : N(-d2));
        double vega = stockPrice * Phi(d1) * Math.Sqrt(timeToExpiry);

        return $"""
            📉 Black-Scholes {optionType.ToUpper()} Option
            Price (Premium): {price:N4}
            Delta  : {delta:F4}
            Gamma  : {gamma:F6}
            Theta  : {theta/365:F4} per day
            Vega   : {vega/100:F4} per 1% vol
            """;
    }

    private static double N(double x) => (1.0 + Erf(x / Math.Sqrt(2))) / 2.0;
    private static double Phi(double x) => Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);
    private static double Erf(double x)
    {
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }
}
