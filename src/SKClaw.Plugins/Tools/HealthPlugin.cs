using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// HealthPlugin — BMI, calorie tracking, workout logging,
/// sleep tracking, hydration, and health metrics calculations.
/// Data stored locally in JSON files.
/// </summary>
public class HealthPlugin
{
    private readonly string _dataDir;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly Kernel _kernel;

    public HealthPlugin(Kernel kernel, string dataDir = "")
    {
        _kernel = kernel;
        _dataDir = string.IsNullOrEmpty(dataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "health")
            : dataDir;
        Directory.CreateDirectory(_dataDir);
    }

    // ── Body Metrics ───────────────────────────────────────────

    [KernelFunction, Description("Calculate BMI (Body Mass Index) and get health category")]
    public string CalculateBmi(
        [Description("Weight in kilograms")] double weightKg,
        [Description("Height in centimeters")] double heightCm)
    {
        double heightM = heightCm / 100.0;
        double bmi = weightKg / (heightM * heightM);
        string category = bmi switch
        {
            < 16 => "Severely Underweight",
            < 18.5 => "Underweight",
            < 25 => "Normal weight",
            < 30 => "Overweight",
            < 35 => "Obese Class I",
            < 40 => "Obese Class II",
            _ => "Obese Class III (Morbidly Obese)"
        };

        double idealLow = 18.5 * heightM * heightM;
        double idealHigh = 24.9 * heightM * heightM;

        return $"""
            ⚖️ BMI Calculation
            Weight   : {weightKg:F1} kg
            Height   : {heightCm:F1} cm
            BMI      : {bmi:F2}
            Category : {category}
            Ideal weight range: {idealLow:F1}–{idealHigh:F1} kg
            """;
    }

    [KernelFunction, Description("Calculate Basal Metabolic Rate (BMR) and daily calorie needs")]
    public string CalculateBmr(
        [Description("Weight in kg")] double weightKg,
        [Description("Height in cm")] double heightCm,
        [Description("Age in years")] int age,
        [Description("Gender: male or female")] string gender,
        [Description("Activity level: sedentary, light, moderate, active, very_active")] string activityLevel = "moderate")
    {
        // Mifflin-St Jeor equation
        double bmr = gender.ToLower() == "female"
            ? (10 * weightKg) + (6.25 * heightCm) - (5 * age) - 161
            : (10 * weightKg) + (6.25 * heightCm) - (5 * age) + 5;

        double multiplier = activityLevel.ToLower() switch
        {
            "sedentary" => 1.2,
            "light" => 1.375,
            "moderate" => 1.55,
            "active" => 1.725,
            "very_active" => 1.9,
            _ => 1.55
        };

        double tdee = bmr * multiplier;
        return $"""
            🔥 Calorie Calculator
            BMR (Base)   : {bmr:F0} kcal/day
            Activity     : {activityLevel} (×{multiplier})
            TDEE (Total) : {tdee:F0} kcal/day
            Weight loss  : {tdee - 500:F0} kcal/day (−0.5 kg/week)
            Weight gain  : {tdee + 500:F0} kcal/day (+0.5 kg/week)
            """;
    }

    [KernelFunction, Description("Calculate body fat percentage using the US Navy Method")]
    public string CalculateBodyFat(
        [Description("Gender: male or female")] string gender,
        [Description("Height in cm")] double heightCm,
        [Description("Waist circumference in cm")] double waistCm,
        [Description("Neck circumference in cm")] double neckCm,
        [Description("Hip circumference in cm (required for female)")] double hipCm = 0)
    {
        double bodyFat;
        if (gender.ToLower() == "female")
        {
            if (hipCm == 0) return "Hip circumference is required for female calculation.";
            bodyFat = 163.205 * Math.Log10(waistCm + hipCm - neckCm) - 97.684 * Math.Log10(heightCm) - 78.387;
        }
        else
        {
            bodyFat = 86.01 * Math.Log10(waistCm - neckCm) - 70.041 * Math.Log10(heightCm) + 36.76;
        }

        string category = gender.ToLower() == "female"
            ? bodyFat switch { < 10 => "Essential fat", < 20 => "Athletic", < 24 => "Fitness", < 31 => "Average", _ => "Obese" }
            : bodyFat switch { < 2 => "Essential fat", < 14 => "Athletic", < 17 => "Fitness", < 24 => "Average", _ => "Obese" };

        return $"""
            🏋️ Body Fat Estimate (US Navy Method)
            Body Fat  : {bodyFat:F1}%
            Category  : {category}
            Fat Mass  : ~{bodyFat / 100 * (waistCm / 2.54 * 2.2 * 0.01):F1} kg (estimated)
            """;
    }

    [KernelFunction, Description("Calculate maximum heart rate and training heart rate zones")]
    public string HeartRateZones(
        [Description("Age in years")] int age,
        [Description("Resting heart rate (BPM)")] int restingHr = 65)
    {
        int maxHr = 220 - age;
        int hrr = maxHr - restingHr; // Heart Rate Reserve (Karvonen)
        string Karvonen(double lo, double hi) => $"{(int)(lo * hrr + restingHr)}–{(int)(hi * hrr + restingHr)} BPM";

        return $"""
            ❤️ Heart Rate Zones (Age: {age})
            Max HR   : {maxHr} BPM  |  Resting HR: {restingHr} BPM
            
            Zone 1 (Recovery/Warm-up) : {Karvonen(0.50, 0.60)} [50-60%]
            Zone 2 (Fat burn/Aerobic) : {Karvonen(0.60, 0.70)} [60-70%]
            Zone 3 (Cardio/Aerobic)   : {Karvonen(0.70, 0.80)} [70-80%]
            Zone 4 (Threshold)        : {Karvonen(0.80, 0.90)} [80-90%]
            Zone 5 (Max effort)       : {Karvonen(0.90, 1.00)} [90-100%]
            """;
    }

    // ── Nutrition ──────────────────────────────────────────────

    [KernelFunction, Description("Calculate macronutrients for a meal (calories from carbs, protein, fat)")]
    public string CalculateMacros(
        [Description("Carbohydrates in grams")] double carbsG,
        [Description("Protein in grams")] double proteinG,
        [Description("Fat in grams")] double fatG,
        [Description("Fiber in grams (optional)")] double fiberG = 0)
    {
        double calories = (carbsG * 4) + (proteinG * 4) + (fatG * 9);
        double netCarbs = carbsG - fiberG;
        double totalMacroG = carbsG + proteinG + fatG;

        return $"""
            🥗 Macronutrient Breakdown
            Carbs   : {carbsG:F1}g ({carbsG * 4:F0} kcal, {(carbsG / totalMacroG * 100):F0}%)
            Protein : {proteinG:F1}g ({proteinG * 4:F0} kcal, {(proteinG / totalMacroG * 100):F0}%)
            Fat     : {fatG:F1}g ({fatG * 9:F0} kcal, {(fatG / totalMacroG * 100):F0}%)
            Fiber   : {fiberG:F1}g
            Net Carbs: {netCarbs:F1}g
            Total Calories: {calories:F0} kcal
            """;
    }

    [KernelFunction, Description("Log a meal or food intake entry")]
    public async Task<string> LogMealAsync(
        [Description("Meal name or description")] string meal,
        [Description("Calories")] int calories,
        [Description("Protein in grams")] double proteinG = 0,
        [Description("Carbs in grams")] double carbsG = 0,
        [Description("Fat in grams")] double fatG = 0,
        [Description("Meal type: breakfast, lunch, dinner, snack")] string mealType = "meal")
    {
        var entries = await LoadListAsync<MealEntry>("meals.json");
        entries.Add(new MealEntry
        {
            Meal = meal,
            Calories = calories,
            ProteinG = proteinG,
            CarbsG = carbsG,
            FatG = fatG,
            Type = mealType,
            LoggedAt = DateTimeOffset.UtcNow
        });
        await SaveListAsync("meals.json", entries);

        var todayTotal = entries.Where(e => e.LoggedAt.Date == DateTime.Today).Sum(e => e.Calories);
        return $"🍽️ Logged: {meal} ({calories} kcal)\nToday's total: {todayTotal} kcal";
    }

    [KernelFunction, Description("Get nutrition summary for today")]
    public async Task<string> GetNutritionSummaryAsync()
    {
        var meals = await LoadListAsync<MealEntry>("meals.json");
        var today = meals.Where(e => e.LoggedAt.Date == DateTime.Today).ToList();

        if (today.Count == 0) return "No meals logged today.";

        double totalCal = today.Sum(e => e.Calories);
        double totalProt = today.Sum(e => e.ProteinG);
        double totalCarb = today.Sum(e => e.CarbsG);
        double totalFat = today.Sum(e => e.FatG);

        var sb = new StringBuilder($"🍽️ Nutrition Summary — {DateTime.Today:yyyy-MM-dd}\n\n");
        foreach (var m in today)
            sb.AppendLine($"  [{m.Type}] {m.Meal}: {m.Calories} kcal");
        sb.AppendLine($"\n  Total: {totalCal:F0} kcal | P:{totalProt:F0}g C:{totalCarb:F0}g F:{totalFat:F0}g");
        return sb.ToString().TrimEnd();
    }

    // ── Workout Logging ────────────────────────────────────────

    [KernelFunction, Description("Log a workout session")]
    public async Task<string> LogWorkoutAsync(
        [Description("Workout type or description, e.g. 'Running 5km' or 'Weight training - chest'")] string workout,
        [Description("Duration in minutes")] int durationMinutes,
        [Description("Estimated calories burned")] int caloriesBurned = 0,
        [Description("Intensity: low, medium, high")] string intensity = "medium",
        [Description("Notes or exercises performed")] string notes = "")
    {
        var workouts = await LoadListAsync<WorkoutEntry>("workouts.json");
        workouts.Add(new WorkoutEntry
        {
            Workout = workout,
            DurationMinutes = durationMinutes,
            CaloriesBurned = caloriesBurned,
            Intensity = intensity,
            Notes = notes,
            LoggedAt = DateTimeOffset.UtcNow
        });
        await SaveListAsync("workouts.json", workouts);

        var weekWorkouts = workouts.Count(w => (DateTime.Today - w.LoggedAt.Date).TotalDays < 7);
        return $"💪 Workout logged: {workout} ({durationMinutes} min, {(caloriesBurned > 0 ? $"{caloriesBurned} kcal" : intensity)})\n7-day total: {weekWorkouts} sessions";
    }

    [KernelFunction, Description("Get workout statistics for a period")]
    public async Task<string> GetWorkoutStatsAsync(
        [Description("Period: week, month, all")] string period = "week")
    {
        var workouts = await LoadListAsync<WorkoutEntry>("workouts.json");
        var filtered = period switch
        {
            "month" => workouts.Where(w => (DateTime.Today - w.LoggedAt.Date).TotalDays < 30).ToList(),
            "all" => workouts,
            _ => workouts.Where(w => (DateTime.Today - w.LoggedAt.Date).TotalDays < 7).ToList()
        };

        if (filtered.Count == 0) return $"No workouts logged for {period}.";

        var totalMin = filtered.Sum(w => w.DurationMinutes);
        var totalCal = filtered.Sum(w => w.CaloriesBurned);
        var sb = new StringBuilder($"💪 Workout Stats ({period}):\n\n");
        sb.AppendLine($"Sessions  : {filtered.Count}");
        sb.AppendLine($"Total time: {totalMin} min ({totalMin / 60}h {totalMin % 60}m)");
        if (totalCal > 0) sb.AppendLine($"Kcal burned: {totalCal}");
        sb.AppendLine("\nRecent sessions:");
        foreach (var w in filtered.OrderByDescending(w => w.LoggedAt).Take(7))
            sb.AppendLine($"  {w.LoggedAt:MM-dd} | {w.DurationMinutes}m | {w.Workout}");
        return sb.ToString().TrimEnd();
    }

    // ── Sleep & Hydration ──────────────────────────────────────

    [KernelFunction, Description("Log a sleep session")]
    public async Task<string> LogSleepAsync(
        [Description("Bedtime (ISO 8601 or HH:mm)")] string bedtime,
        [Description("Wake time (ISO 8601 or HH:mm)")] string wakeTime,
        [Description("Sleep quality: 1-5 (5=best)")] int quality = 3,
        [Description("Notes")] string notes = "")
    {
        var sleepLogs = await LoadListAsync<SleepEntry>("sleep.json");
        var bed = ParseTimeOrDateTime(bedtime);
        var wake = ParseTimeOrDateTime(wakeTime);
        if (wake < bed) wake = wake.AddDays(1);
        var hours = (wake - bed).TotalHours;

        sleepLogs.Add(new SleepEntry { Bedtime = bed, WakeTime = wake, Hours = hours, Quality = quality, Notes = notes });
        await SaveListAsync("sleep.json", sleepLogs);

        string emoji = hours switch { >= 8 => "😴", >= 7 => "🙂", >= 6 => "😐", _ => "😴💤" };
        return $"{emoji} Sleep logged: {hours:F1} hours (quality: {quality}/5)";
    }

    [KernelFunction, Description("Log daily water intake")]
    public async Task<string> LogWaterAsync(
        [Description("Amount in ml")] int ml,
        [Description("Note (e.g. 'after workout')")] string note = "")
    {
        var logs = await LoadListAsync<WaterEntry>("water.json");
        logs.Add(new WaterEntry { Ml = ml, Note = note, LoggedAt = DateTimeOffset.UtcNow });
        await SaveListAsync("water.json", logs);

        var todayMl = logs.Where(w => w.LoggedAt.Date == DateTime.Today).Sum(w => w.Ml);
        double pct = todayMl / 2000.0 * 100;
        var bar = "[" + new string('█', (int)(pct / 10)) + new string('░', 10 - (int)(pct / 10)) + "]";
        return $"💧 +{ml}ml logged. Today: {todayMl}ml / 2000ml {bar} {pct:F0}%";
    }

    // ── Helpers ────────────────────────────────────────────────
    private async Task<List<T>> LoadListAsync<T>(string file)
    {
        var path = Path.Combine(_dataDir, file);
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(path), _json) ?? [];
    }

    private async Task SaveListAsync<T>(string file, List<T> data)
        => await File.WriteAllTextAsync(Path.Combine(_dataDir, file), JsonSerializer.Serialize(data, _json));

    private static DateTimeOffset ParseTimeOrDateTime(string s)
    {
        if (TimeSpan.TryParse(s, out var ts))
            return DateTimeOffset.Now.Date.Add(ts);
        if (DateTimeOffset.TryParse(s, out var dt))
            return dt;
        return DateTimeOffset.Now;
    }
}

public class MealEntry { public string Meal { get; set; } = ""; public int Calories { get; set; } public double ProteinG { get; set; } public double CarbsG { get; set; } public double FatG { get; set; } public string Type { get; set; } = "meal"; public DateTimeOffset LoggedAt { get; set; } }
public class WorkoutEntry { public string Workout { get; set; } = ""; public int DurationMinutes { get; set; } public int CaloriesBurned { get; set; } public string Intensity { get; set; } = "medium"; public string Notes { get; set; } = ""; public DateTimeOffset LoggedAt { get; set; } }
public class SleepEntry { public DateTimeOffset Bedtime { get; set; } public DateTimeOffset WakeTime { get; set; } public double Hours { get; set; } public int Quality { get; set; } public string Notes { get; set; } = ""; }
public class WaterEntry { public int Ml { get; set; } public string Note { get; set; } = ""; public DateTimeOffset LoggedAt { get; set; } }
