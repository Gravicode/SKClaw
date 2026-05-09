using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// MathSkill — Full arithmetic, statistics, number theory,
/// unit conversion, financial math, and geometry.
/// </summary>
public class MathSkill
{
    // ── Expression Evaluation ──────────────────────────────────

    [KernelFunction, Description("Evaluate a mathematical expression. Supports +, -, *, /, %, ^, sqrt, abs, round, floor, ceiling, log, sin, cos, tan, pi, e")]
    public string Calculate([Description("Expression, e.g. '2^10', 'sqrt(144)', 'sin(PI/2)'")] string expression)
    {
        try
        {
            var result = EvalExpression(expression);
            return double.IsNaN(result) || double.IsInfinity(result)
                ? $"Result: {result}"
                : $"{result:G15}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Evaluate multiple expressions at once and return a table of results")]
    public string CalculateMultiple([Description("Expressions separated by newlines or semicolons")] string expressions)
    {
        var list = expressions.Split(new[] {'\n', ';'}, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var expr in list)
        {
            var clean = expr.Trim();
            try { sb.AppendLine($"{clean} = {EvalExpression(clean):G10}"); }
            catch (Exception ex) { sb.AppendLine($"{clean} = ERROR: {ex.Message}"); }
        }
        return sb.ToString().Trim();
    }

    // ── Unit Conversion ────────────────────────────────────────

    [KernelFunction, Description("Convert between units. Categories: length, mass, temperature, area, volume, speed, pressure, energy, data, angle")]
    public string ConvertUnit(
        [Description("Value to convert")] double value,
        [Description("Source unit, e.g. km, kg, celsius, sqm, liter, mph, bar, joule, mb, degree")] string from,
        [Description("Target unit")] string to)
    {
        from = from.ToLower().Trim();
        to   = to.ToLower().Trim();

        // All conversions normalised through SI base unit
        var convTable = new Dictionary<string, double>
        {
            // Length → metre
            {"m",1},{"km",1000},{"cm",0.01},{"mm",0.001},{"um",1e-6},{"nm",1e-9},
            {"mi",1609.344},{"miles",1609.344},{"yard",0.9144},{"yd",0.9144},
            {"ft",0.3048},{"feet",0.3048},{"inch",0.0254},{"in",0.0254},
            {"nautical_mile",1852},{"nmi",1852},
            // Mass → kg
            {"kg",1},{"g",0.001},{"mg",1e-6},{"t",1000},{"tonne",1000},
            {"lb",0.453592},{"lbs",0.453592},{"oz",0.028350},
            {"stone",6.35029},
            // Area → m²
            {"sqm",1},{"m2",1},{"sqkm",1e6},{"km2",1e6},
            {"sqcm",0.0001},{"sqft",0.092903},{"sqyd",0.836127},
            {"acre",4046.856},{"hectare",10000},{"ha",10000},
            // Volume → litre
            {"l",1},{"liter",1},{"litre",1},{"ml",0.001},
            {"cl",0.01},{"dl",0.1},{"m3",1000},{"cbm",1000},
            {"gallon",3.78541},{"gal",3.78541},{"quart",0.946353},
            {"pint",0.473176},{"cup",0.236588},{"fl_oz",0.029574},
            {"tbsp",0.014787},{"tsp",0.004929},
            // Speed → m/s
            {"mps",1},{"kph",0.277778},{"kmh",0.277778},
            {"mph",0.44704},{"knot",0.514444},{"fps",0.3048},
            // Pressure → Pascal
            {"pa",1},{"kpa",1000},{"mpa",1e6},{"bar",1e5},
            {"mbar",100},{"psi",6894.757},{"atm",101325},{"mmhg",133.322},
            // Energy → Joule
            {"j",1},{"kj",1000},{"mj",1e6},{"gj",1e9},
            {"cal",4.184},{"kcal",4184},{"wh",3600},{"kwh",3.6e6},
            {"btu",1055.056},{"ev",1.60218e-19},
            // Data → bytes
            {"b",1},{"byte",1},{"kb",1024},{"mb",1048576},
            {"gb",1073741824},{"tb",1.099512e12},{"pb",1.125900e15},
            {"kib",1024},{"mib",1048576},{"gib",1073741824},
            // Angle → degree
            {"deg",1},{"degree",1},{"rad",57.29578},{"radian",57.29578},
            {"grad",0.9},{"turn",360},
        };

        // Temperature handled separately
        if (new[]{"celsius","fahrenheit","kelvin","c","f","k"}.Contains(from) ||
            new[]{"celsius","fahrenheit","kelvin","c","f","k"}.Contains(to))
            return ConvertTemp(value, from, to);

        if (!convTable.TryGetValue(from, out var fVal))
            return $"Unknown unit: '{from}'";
        if (!convTable.TryGetValue(to, out var tVal))
            return $"Unknown unit: '{to}'";

        double inSI = value * fVal;
        double result = inSI / tVal;
        return $"{value:G10} {from} = {result:G10} {to}";
    }

    // ── Statistics ─────────────────────────────────────────────

    [KernelFunction, Description("Calculate descriptive statistics for a list of numbers: mean, median, mode, std dev, variance, min, max, range, percentiles")]
    public string Statistics([Description("Comma or space separated list of numbers")] string numbers)
    {
        var data = ParseNumbers(numbers);
        if (data.Length == 0) return "No valid numbers found.";

        Array.Sort(data);
        double mean    = data.Average();
        double median  = Median(data);
        double std     = StdDev(data, mean);
        double variance = std * std;
        var mode       = Mode(data);
        double p25     = Percentile(data, 25);
        double p75     = Percentile(data, 75);
        double iqr     = p75 - p25;

        return $"""
            Count   : {data.Length}
            Sum     : {data.Sum():G10}
            Mean    : {mean:G10}
            Median  : {median:G10}
            Mode    : {string.Join(", ", mode.Select(x => x.ToString("G6")))}
            Std Dev : {std:G10}
            Variance: {variance:G10}
            Min     : {data.Min():G10}
            Max     : {data.Max():G10}
            Range   : {data.Max() - data.Min():G10}
            P25     : {p25:G10}
            P75     : {p75:G10}
            IQR     : {iqr:G10}
            """;
    }

    [KernelFunction, Description("Calculate correlation coefficient between two lists of numbers")]
    public string Correlation(
        [Description("First list of numbers (comma-separated)")] string xValues,
        [Description("Second list of numbers (comma-separated)")] string yValues)
    {
        var x = ParseNumbers(xValues);
        var y = ParseNumbers(yValues);
        if (x.Length != y.Length) return "Lists must have equal length.";
        if (x.Length < 2) return "Need at least 2 values.";

        double xMean = x.Average(), yMean = y.Average();
        double num = x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
        double denX = Math.Sqrt(x.Sum(xi => Math.Pow(xi - xMean, 2)));
        double denY = Math.Sqrt(y.Sum(yi => Math.Pow(yi - yMean, 2)));
        double r = num / (denX * denY);

        string interp = Math.Abs(r) switch
        {
            >= 0.9  => "very strong",
            >= 0.7  => "strong",
            >= 0.5  => "moderate",
            >= 0.3  => "weak",
            _       => "very weak"
        };
        return $"Pearson r = {r:F6} ({interp} {(r >= 0 ? "positive" : "negative")} correlation)";
    }

    [KernelFunction, Description("Perform linear regression on x,y data and return the line equation")]
    public string LinearRegression(
        [Description("X values (comma-separated)")] string xValues,
        [Description("Y values (comma-separated)")] string yValues)
    {
        var x = ParseNumbers(xValues);
        var y = ParseNumbers(yValues);
        if (x.Length != y.Length || x.Length < 2) return "Need equal-length lists with at least 2 values.";

        double n = x.Length;
        double xMean = x.Average(), yMean = y.Average();
        double slope = x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum() /
                       x.Sum(xi => Math.Pow(xi - xMean, 2));
        double intercept = yMean - slope * xMean;
        double r2 = Math.Pow(slope * x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum() /
                             (x.Sum(xi => Math.Pow(xi - xMean, 2)) * y.Sum(yi => Math.Pow(yi - yMean, 2))), 2);

        return $"y = {slope:G6}x + {intercept:G6}\nR² = {r2:F4}  (slope={slope:G6}, intercept={intercept:G6})";
    }

    // ── Financial Math ─────────────────────────────────────────

    [KernelFunction, Description("Calculate compound interest")]
    public string CompoundInterest(
        [Description("Principal amount")] double principal,
        [Description("Annual interest rate as percentage, e.g. 5 for 5%")] double annualRatePercent,
        [Description("Number of years")] double years,
        [Description("Compounding frequency per year: 1=annually, 12=monthly, 365=daily")] int compoundsPerYear = 12)
    {
        double r = annualRatePercent / 100.0;
        double amount = principal * Math.Pow(1 + r / compoundsPerYear, compoundsPerYear * years);
        double interest = amount - principal;
        return $"""
            Principal  : {principal:N2}
            Rate       : {annualRatePercent}% p.a.
            Period     : {years} years (compounded {compoundsPerYear}x/year)
            Final Value: {amount:N2}
            Interest   : {interest:N2}
            """;
    }

    [KernelFunction, Description("Calculate loan / mortgage monthly payment (PMT)")]
    public string LoanPayment(
        [Description("Loan principal")] double principal,
        [Description("Annual interest rate as percentage")] double annualRatePercent,
        [Description("Loan term in months")] int termMonths)
    {
        double r = annualRatePercent / 100.0 / 12.0;
        if (r == 0) return $"Monthly payment: {principal / termMonths:N2}";
        double pmt = principal * r * Math.Pow(1 + r, termMonths) / (Math.Pow(1 + r, termMonths) - 1);
        double total = pmt * termMonths;
        return $"""
            Principal      : {principal:N2}
            Rate           : {annualRatePercent}% p.a. ({r * 100:F4}% /month)
            Term           : {termMonths} months ({termMonths / 12.0:F1} years)
            Monthly Payment: {pmt:N2}
            Total Payment  : {total:N2}
            Total Interest : {total - principal:N2}
            """;
    }

    [KernelFunction, Description("Calculate ROI (Return on Investment)")]
    public string ROI(
        [Description("Initial investment cost")] double cost,
        [Description("Final value / revenue")] double revenue)
    {
        double roi = (revenue - cost) / cost * 100;
        double profit = revenue - cost;
        return $"Cost={cost:N2}  Revenue={revenue:N2}  Profit={profit:N2}  ROI={roi:F2}%";
    }

    [KernelFunction, Description("Calculate percentage: what percent is X of Y, X% of Y, increase/decrease")]
    public string Percentage(
        [Description("Type: 'of' (X% of Y), 'what' (X is what % of Y), 'change' (% change from X to Y), 'add' (Y + X%), 'sub' (Y - X%)")] string type,
        [Description("First value (X)")] double x,
        [Description("Second value (Y)")] double y)
    {
        return type.ToLower() switch
        {
            "of"     => $"{x}% of {y} = {x / 100 * y:G10}",
            "what"   => $"{x} is {x / y * 100:F4}% of {y}",
            "change" => $"% change from {x} to {y} = {(y - x) / x * 100:F4}% ({(y > x ? "increase" : "decrease")})",
            "add"    => $"{y} + {x}% = {y * (1 + x / 100):G10}",
            "sub"    => $"{y} - {x}% = {y * (1 - x / 100):G10}",
            _        => "type must be: of | what | change | add | sub"
        };
    }

    // ── Number Theory ──────────────────────────────────────────

    [KernelFunction, Description("Check if a number is prime and find prime factors")]
    public string PrimeInfo([Description("Integer to analyse")] long n)
    {
        bool isPrime = IsPrime(n);
        var factors = PrimeFactors(n);
        return $"{n} is {(isPrime ? "" : "NOT ")}prime. Prime factors: {string.Join(" × ", factors)}";
    }

    [KernelFunction, Description("Find prime numbers up to a limit using Sieve of Eratosthenes")]
    public string FindPrimes([Description("Upper limit (max 100000)")] int limit)
    {
        limit = Math.Min(limit, 100_000);
        var sieve = new bool[limit + 1];
        Array.Fill(sieve, true);
        sieve[0] = sieve[1] = false;
        for (int i = 2; i * i <= limit; i++)
            if (sieve[i])
                for (int j = i * i; j <= limit; j += i) sieve[j] = false;
        var primes = Enumerable.Range(2, limit - 1).Where(i => sieve[i]).ToList();
        return $"Found {primes.Count} primes up to {limit}. First 20: {string.Join(", ", primes.Take(20))}{(primes.Count > 20 ? "..." : "")}";
    }

    [KernelFunction, Description("Calculate GCD and LCM of two or more integers")]
    public string GcdLcm([Description("Comma-separated list of integers")] string numbers)
    {
        var nums = ParseLongs(numbers);
        if (nums.Length < 2) return "Need at least 2 numbers.";
        long gcd = nums.Aggregate(Gcd);
        long lcm = nums.Aggregate(Lcm);
        return $"Numbers: {string.Join(", ", nums)}\nGCD: {gcd}\nLCM: {lcm}";
    }

    [KernelFunction, Description("Convert number between bases (binary, octal, decimal, hex)")]
    public string ConvertBase(
        [Description("Number as string in the source base")] string number,
        [Description("Source base: 2, 8, 10, 16")] int fromBase,
        [Description("Target base: 2, 8, 10, 16")] int toBase)
    {
        try
        {
            long val = Convert.ToInt64(number.Replace("0x","").Replace("0b",""), fromBase);
            string result = toBase switch
            {
                2  => Convert.ToString(val, 2),
                8  => Convert.ToString(val, 8),
                10 => val.ToString(),
                16 => val.ToString("X"),
                _  => "Unsupported base"
            };
            string prefix = toBase switch { 2 => "0b", 8 => "0o", 16 => "0x", _ => "" };
            return $"{number} (base {fromBase}) = {prefix}{result} (base {toBase})";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [KernelFunction, Description("Generate Fibonacci sequence up to N terms")]
    public string Fibonacci([Description("Number of terms (1-100)")] int n)
    {
        n = Math.Clamp(n, 1, 100);
        var seq = new List<long> { 0, 1 };
        for (int i = 2; i < n; i++) seq.Add(seq[^1] + seq[^2]);
        return $"Fibonacci({n}): {string.Join(", ", seq.Take(n))}";
    }

    // ── Geometry ───────────────────────────────────────────────

    [KernelFunction, Description("Calculate area, perimeter/circumference for common 2D shapes")]
    public string ShapeArea(
        [Description("Shape: circle, rectangle, square, triangle, trapezoid, ellipse, rhombus")] string shape,
        [Description("Dimensions as comma-separated values. circle: radius. rectangle: width,height. square: side. triangle: base,height. trapezoid: a,b,height. ellipse: a,b. rhombus: d1,d2")] string dimensions)
    {
        var d = ParseNumbers(dimensions);
        return shape.ToLower() switch
        {
            "circle"    when d.Length >= 1 => $"Circle r={d[0]}: Area={Math.PI * d[0] * d[0]:G8}, Circumference={2 * Math.PI * d[0]:G8}",
            "rectangle" when d.Length >= 2 => $"Rectangle {d[0]}×{d[1]}: Area={d[0]*d[1]:G8}, Perimeter={2*(d[0]+d[1]):G8}",
            "square"    when d.Length >= 1 => $"Square {d[0]}: Area={d[0]*d[0]:G8}, Perimeter={4*d[0]:G8}",
            "triangle"  when d.Length >= 2 => $"Triangle base={d[0]} height={d[1]}: Area={0.5*d[0]*d[1]:G8}",
            "trapezoid" when d.Length >= 3 => $"Trapezoid a={d[0]} b={d[1]} h={d[2]}: Area={(d[0]+d[1])/2*d[2]:G8}",
            "ellipse"   when d.Length >= 2 => $"Ellipse a={d[0]} b={d[1]}: Area={Math.PI*d[0]*d[1]:G8}, Perimeter≈{2*Math.PI*Math.Sqrt((d[0]*d[0]+d[1]*d[1])/2):G8}",
            "rhombus"   when d.Length >= 2 => $"Rhombus d1={d[0]} d2={d[1]}: Area={0.5*d[0]*d[1]:G8}",
            _ => "Invalid shape or dimensions. See description for format."
        };
    }

    [KernelFunction, Description("Calculate volume and surface area for 3D shapes")]
    public string ShapeVolume(
        [Description("Shape: sphere, cube, cylinder, cone, pyramid, box")] string shape,
        [Description("Dimensions comma-separated. sphere: r. cube: side. cylinder: r,h. cone: r,h. pyramid: base,height. box: l,w,h")] string dimensions)
    {
        var d = ParseNumbers(dimensions);
        return shape.ToLower() switch
        {
            "sphere"   when d.Length >= 1 => $"Sphere r={d[0]}: Volume={4.0/3*Math.PI*Math.Pow(d[0],3):G8}, SurfaceArea={4*Math.PI*d[0]*d[0]:G8}",
            "cube"     when d.Length >= 1 => $"Cube side={d[0]}: Volume={Math.Pow(d[0],3):G8}, SurfaceArea={6*d[0]*d[0]:G8}",
            "cylinder" when d.Length >= 2 => $"Cylinder r={d[0]} h={d[1]}: Volume={Math.PI*d[0]*d[0]*d[1]:G8}, SurfaceArea={2*Math.PI*d[0]*(d[0]+d[1]):G8}",
            "cone"     when d.Length >= 2 => $"Cone r={d[0]} h={d[1]}: Volume={Math.PI*d[0]*d[0]*d[1]/3:G8}, SurfaceArea={Math.PI*d[0]*(d[0]+Math.Sqrt(d[0]*d[0]+d[1]*d[1])):G8}",
            "box"      when d.Length >= 3 => $"Box {d[0]}×{d[1]}×{d[2]}: Volume={d[0]*d[1]*d[2]:G8}, SurfaceArea={2*(d[0]*d[1]+d[1]*d[2]+d[0]*d[2]):G8}",
            _ => "Invalid shape or dimensions."
        };
    }

    [KernelFunction, Description("Solve a quadratic equation ax² + bx + c = 0")]
    public string SolveQuadratic(
        [Description("Coefficient a")] double a,
        [Description("Coefficient b")] double b,
        [Description("Coefficient c")] double c)
    {
        double discriminant = b * b - 4 * a * c;
        if (a == 0) return b == 0 ? (c == 0 ? "All real numbers" : "No solution") : $"Linear solution: x = {-c / b:G10}";
        if (discriminant > 0)
        {
            double x1 = (-b + Math.Sqrt(discriminant)) / (2 * a);
            double x2 = (-b - Math.Sqrt(discriminant)) / (2 * a);
            return $"Two real roots: x₁={x1:G10}, x₂={x2:G10}";
        }
        if (discriminant == 0) return $"One real root: x={-b / (2 * a):G10}";
        double re = -b / (2 * a), im = Math.Sqrt(-discriminant) / (2 * a);
        return $"Two complex roots: x={re:G8} ± {im:G8}i";
    }

    // ── Helpers ────────────────────────────────────────────────
    private static double[] ParseNumbers(string s) =>
        Regex.Matches(s, @"-?\d+(\.\d+)?([eE][+-]?\d+)?")
             .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
             .ToArray();

    private static long[] ParseLongs(string s) =>
        Regex.Matches(s, @"-?\d+").Select(m => long.Parse(m.Value)).ToArray();

    private static double Median(double[] sorted) =>
        sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
            : sorted[sorted.Length / 2];

    private static double StdDev(double[] data, double mean) =>
        Math.Sqrt(data.Sum(x => Math.Pow(x - mean, 2)) / (data.Length - 1));

    private static double[] Mode(double[] sorted)
    {
        var freq = sorted.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList();
        int maxFreq = freq.First().Count();
        return maxFreq == 1 ? Array.Empty<double>() : freq.Where(g => g.Count() == maxFreq).Select(g => g.Key).ToArray();
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return double.NaN;
        double idx = (p / 100) * (sorted.Length - 1);
        int lo = (int)idx;
        double frac = idx - lo;
        return lo + 1 < sorted.Length ? sorted[lo] + frac * (sorted[lo + 1] - sorted[lo]) : sorted[lo];
    }

    private static bool IsPrime(long n)
    {
        if (n < 2) return false;
        if (n < 4) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (long i = 5; i * i <= n; i += 6)
            if (n % i == 0 || n % (i + 2) == 0) return false;
        return true;
    }

    private static List<long> PrimeFactors(long n)
    {
        var factors = new List<long>();
        if (n < 2) return factors;
        for (long f = 2; f * f <= n; f++)
            while (n % f == 0) { factors.Add(f); n /= f; }
        if (n > 1) factors.Add(n);
        return factors;
    }

    private static long Gcd(long a, long b) { while (b != 0) { (a, b) = (b, a % b); } return Math.Abs(a); }
    private static long Lcm(long a, long b) => Math.Abs(a / Gcd(a, b) * b);

    private static string ConvertTemp(double v, string from, string to)
    {
        double celsius = from switch
        {
            "celsius" or "c" => v,
            "fahrenheit" or "f" => (v - 32) * 5 / 9,
            "kelvin" or "k" => v - 273.15,
            _ => v
        };
        double result = to switch
        {
            "celsius" or "c" => celsius,
            "fahrenheit" or "f" => celsius * 9 / 5 + 32,
            "kelvin" or "k" => celsius + 273.15,
            _ => celsius
        };
        return $"{v} {from} = {result:G10} {to}";
    }

    private static double EvalExpression(string expr)
    {
        expr = Regex.Replace(expr, @"\bpi\b", Math.PI.ToString("R"), RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\be\b", Math.E.ToString("R"), RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"sqrt\(([^)]+)\)", m => Math.Sqrt(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"abs\(([^)]+)\)", m => Math.Abs(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"log\(([^)]+)\)", m => Math.Log10(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"ln\(([^)]+)\)", m => Math.Log(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"sin\(([^)]+)\)", m => Math.Sin(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"cos\(([^)]+)\)", m => Math.Cos(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"tan\(([^)]+)\)", m => Math.Tan(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"floor\(([^)]+)\)", m => Math.Floor(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"ceiling\(([^)]+)\)", m => Math.Ceiling(double.Parse(EvalExpression(m.Groups[1].Value).ToString())).ToString("R"));
        expr = Regex.Replace(expr, @"round\(([^)]+)\)", m => Math.Round(double.Parse(EvalExpression(m.Groups[1].Value).ToString()), 6).ToString("R"));
        expr = expr.Replace("^", "**").Replace("×","*").Replace("÷","/");
        var dt = new System.Data.DataTable();
        return Convert.ToDouble(dt.Compute(expr, null));
    }
}
