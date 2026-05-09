using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// ProductivityPlugin — Local task management (to-do lists), notes/journal,
/// pomodoro timer tracking, habit tracker, goal management, and scheduling helpers.
/// All data persisted locally as JSON in the workspace directory.
/// </summary>
public class ProductivityPlugin
{
    private readonly string _dataDir;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ProductivityPlugin(string dataDir = "")
    {
        _dataDir = string.IsNullOrEmpty(dataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".skclaw", "productivity")
            : dataDir;
        Directory.CreateDirectory(_dataDir);
    }

    // ── Task Management ────────────────────────────────────────

    [KernelFunction, Description("Add a new task to the to-do list")]
    public async Task<string> AddTaskAsync(
        [Description("Task title")] string title,
        [Description("Priority: high, medium, low")] string priority = "medium",
        [Description("Due date (ISO 8601 or natural date like 'tomorrow', 'friday')")] string dueDate = "",
        [Description("Tags or category, e.g. work, personal, shopping")] string tags = "",
        [Description("Task description or notes")] string notes = "")
    {
        var tasks = await LoadJsonListAsync<TaskItem>("tasks.json");
        var due = ParseNaturalDate(dueDate);
        var task = new TaskItem
        {
            Id       = Guid.NewGuid().ToString("N")[..8],
            Title    = title,
            Priority = priority,
            DueDate  = due,
            Tags     = tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
            Notes    = notes,
            CreatedAt = DateTimeOffset.UtcNow,
            Status   = "todo"
        };
        tasks.Add(task);
        await SaveJsonListAsync("tasks.json", tasks);
        return $"✅ Task added: [{task.Id}] {title} (Priority: {priority}{(due.HasValue ? $", Due: {due:yyyy-MM-dd}" : "")})";
    }

    [KernelFunction, Description("List tasks with optional filters")]
    public async Task<string> ListTasksAsync(
        [Description("Filter by status: all, todo, in_progress, done")] string status = "todo",
        [Description("Filter by priority: all, high, medium, low")] string priority = "all",
        [Description("Filter by tag")] string tag = "",
        [Description("Show overdue only?")] bool overdueOnly = false)
    {
        var tasks = await LoadJsonListAsync<TaskItem>("tasks.json");
        var filtered = tasks.AsEnumerable();

        if (status != "all") filtered = filtered.Where(t => t.Status == status);
        if (priority != "all") filtered = filtered.Where(t => t.Priority == priority);
        if (!string.IsNullOrEmpty(tag)) filtered = filtered.Where(t => t.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        if (overdueOnly) filtered = filtered.Where(t => t.DueDate.HasValue && t.DueDate < DateTimeOffset.UtcNow && t.Status != "done");

        var list = filtered.OrderBy(t => t.Priority switch { "high" => 0, "medium" => 1, _ => 2 })
                           .ThenBy(t => t.DueDate ?? DateTimeOffset.MaxValue).ToList();

        if (list.Count == 0) return "No tasks found matching the criteria.";

        var sb = new StringBuilder($"📋 Tasks ({list.Count}):\n\n");
        foreach (var t in list)
        {
            var pri = t.Priority switch { "high" => "🔴", "medium" => "🟡", _ => "🟢" };
            var due = t.DueDate.HasValue ? $" | Due: {t.DueDate:yyyy-MM-dd}" : "";
            var overdue = t.DueDate.HasValue && t.DueDate < DateTimeOffset.UtcNow && t.Status != "done" ? " ⚠️OVERDUE" : "";
            var tags = t.Tags.Count > 0 ? $" [{string.Join(", ", t.Tags)}]" : "";
            sb.AppendLine($"{pri} [{t.Id}] [{t.Status.ToUpper()}] {t.Title}{due}{overdue}{tags}");
        }
        return sb.ToString().TrimEnd();
    }

    [KernelFunction, Description("Update the status of a task")]
    public async Task<string> UpdateTaskStatusAsync(
        [Description("Task ID")] string taskId,
        [Description("New status: todo, in_progress, done, cancelled")] string newStatus)
    {
        var tasks = await LoadJsonListAsync<TaskItem>("tasks.json");
        var task = tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return $"Task '{taskId}' not found.";

        task.Status = newStatus;
        if (newStatus == "done") task.CompletedAt = DateTimeOffset.UtcNow;
        await SaveJsonListAsync("tasks.json", tasks);
        return $"✅ Task [{taskId}] '{task.Title}' → {newStatus}";
    }

    [KernelFunction, Description("Delete a task by ID")]
    public async Task<string> DeleteTaskAsync([Description("Task ID")] string taskId)
    {
        var tasks = await LoadJsonListAsync<TaskItem>("tasks.json");
        var task = tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return $"Task '{taskId}' not found.";
        tasks.Remove(task);
        await SaveJsonListAsync("tasks.json", tasks);
        return $"🗑️ Task [{taskId}] '{task.Title}' deleted.";
    }

    [KernelFunction, Description("Get a productivity summary: task counts by status, priority, overdue")]
    public async Task<string> GetProductivitySummaryAsync()
    {
        var tasks  = await LoadJsonListAsync<TaskItem>("tasks.json");
        var habits = await LoadJsonListAsync<HabitEntry>("habits.json");
        var notes  = await LoadJsonListAsync<NoteItem>("notes.json");

        var todo   = tasks.Count(t => t.Status == "todo");
        var inProg = tasks.Count(t => t.Status == "in_progress");
        var done   = tasks.Count(t => t.Status == "done");
        var high   = tasks.Count(t => t.Priority == "high" && t.Status != "done");
        var overdue = tasks.Count(t => t.DueDate.HasValue && t.DueDate < DateTimeOffset.UtcNow && t.Status != "done");
        var todayHabits = habits.Count(h => h.Date.Date == DateTime.Today);

        return $"""
            📊 Productivity Summary — {DateTime.Today:dddd, MMMM d yyyy}
            
            Tasks:
              Todo      : {todo}
              In Progress: {inProg}
              Done      : {done}
              High Priority: {high}
              Overdue   : {overdue}
            
            Notes/Journal: {notes.Count} entries
            Habit check-ins today: {todayHabits}
            """;
    }

    // ── Notes & Journal ────────────────────────────────────────

    [KernelFunction, Description("Add a note or journal entry")]
    public async Task<string> AddNoteAsync(
        [Description("Note title")] string title,
        [Description("Note content")] string content,
        [Description("Tags (comma-separated)")] string tags = "",
        [Description("Note type: note, journal, idea, reference")] string type = "note")
    {
        var notes = await LoadJsonListAsync<NoteItem>("notes.json");
        var note = new NoteItem
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Title     = title,
            Content   = content,
            Tags      = tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
            Type      = type,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        notes.Add(note);
        await SaveJsonListAsync("notes.json", notes);
        return $"📝 Note saved: [{note.Id}] {title}";
    }

    [KernelFunction, Description("Search notes by keyword or tag")]
    public async Task<string> SearchNotesAsync(
        [Description("Search keyword (searches title and content)")] string keyword = "",
        [Description("Filter by tag")] string tag = "",
        [Description("Filter by type: note, journal, idea, reference, all")] string type = "all",
        [Description("Max results")] int maxResults = 10)
    {
        var notes = await LoadJsonListAsync<NoteItem>("notes.json");
        var filtered = notes.AsEnumerable();

        if (!string.IsNullOrEmpty(keyword))
            filtered = filtered.Where(n =>
                n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                n.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(tag))
            filtered = filtered.Where(n => n.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        if (type != "all")
            filtered = filtered.Where(n => n.Type == type);

        var list = filtered.OrderByDescending(n => n.UpdatedAt).Take(maxResults).ToList();
        if (list.Count == 0) return "No notes found.";

        var sb = new StringBuilder($"📝 Notes ({list.Count}):\n\n");
        foreach (var note in list)
        {
            var preview = note.Content.Length > 120 ? note.Content[..120] + "..." : note.Content;
            var tags_ = note.Tags.Count > 0 ? $" [{string.Join(", ", note.Tags)}]" : "";
            sb.AppendLine($"[{note.Id}] [{note.Type}] {note.Title}{tags_}");
            sb.AppendLine($"  {preview}");
            sb.AppendLine($"  {note.UpdatedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    [KernelFunction, Description("Read the full content of a note by ID")]
    public async Task<string> ReadNoteAsync([Description("Note ID")] string noteId)
    {
        var notes = await LoadJsonListAsync<NoteItem>("notes.json");
        var note = notes.FirstOrDefault(n => n.Id == noteId);
        if (note == null) return $"Note '{noteId}' not found.";
        return $"""
            [{note.Type.ToUpper()}] {note.Title}
            Tags: {string.Join(", ", note.Tags)}
            Created: {note.CreatedAt:yyyy-MM-dd HH:mm}
            Updated: {note.UpdatedAt:yyyy-MM-dd HH:mm}
            ---
            {note.Content}
            """;
    }

    // ── Habit Tracker ──────────────────────────────────────────

    [KernelFunction, Description("Define a new habit to track")]
    public async Task<string> AddHabitAsync(
        [Description("Habit name")] string name,
        [Description("Frequency: daily, weekdays, weekly")] string frequency = "daily",
        [Description("Target count per period")] int target = 1,
        [Description("Goal or motivation")] string goal = "")
    {
        var habits = await LoadJsonListAsync<HabitDefinition>("habit_defs.json");
        if (habits.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return $"Habit '{name}' already exists.";

        habits.Add(new HabitDefinition { Id = Guid.NewGuid().ToString("N")[..8], Name = name, Frequency = frequency, Target = target, Goal = goal, CreatedAt = DateTimeOffset.UtcNow });
        await SaveJsonListAsync("habit_defs.json", habits);
        return $"✅ Habit added: {name} ({frequency}, target: {target}x)";
    }

    [KernelFunction, Description("Log a habit check-in for today")]
    public async Task<string> CheckInHabitAsync(
        [Description("Habit name")] string habitName,
        [Description("Count (default 1)")] int count = 1,
        [Description("Optional note")] string note = "")
    {
        var entries = await LoadJsonListAsync<HabitEntry>("habits.json");
        entries.Add(new HabitEntry { HabitName = habitName, Date = DateTime.Today, Count = count, Note = note });
        await SaveJsonListAsync("habits.json", entries);

        // Compute streak
        var streak = ComputeStreak(entries, habitName);
        return $"✅ Checked in: {habitName} x{count}" +
               (streak > 1 ? $" 🔥 {streak}-day streak!" : "") +
               (!string.IsNullOrEmpty(note) ? $" [{note}]" : "");
    }

    [KernelFunction, Description("Get habit tracking report for the last N days")]
    public async Task<string> GetHabitReportAsync(
        [Description("Number of days to include (7, 14, 30)")] int days = 7)
    {
        var defs    = await LoadJsonListAsync<HabitDefinition>("habit_defs.json");
        var entries = await LoadJsonListAsync<HabitEntry>("habits.json");

        if (defs.Count == 0) return "No habits defined. Use AddHabit first.";

        var sb = new StringBuilder($"📊 Habit Report — Last {days} Days\n\n");
        var startDate = DateTime.Today.AddDays(-days + 1);

        foreach (var def in defs)
        {
            var relevant = entries.Where(e => e.HabitName.Equals(def.Name, StringComparison.OrdinalIgnoreCase) && e.Date >= startDate).ToList();
            var daysCompleted = relevant.Select(e => e.Date.Date).Distinct().Count();
            var streak = ComputeStreak(entries, def.Name);
            var pct = (double)daysCompleted / days * 100;

            sb.AppendLine($"  {def.Name}: {daysCompleted}/{days} days ({pct:F0}%) | Streak: {streak} 🔥");

            // Mini calendar (last 14 days)
            var calendar = new StringBuilder("  [");
            for (int d = days - 1; d >= 0; d--)
            {
                var date = DateTime.Today.AddDays(-d);
                var done = relevant.Any(e => e.Date.Date == date.Date);
                calendar.Append(done ? "■" : "·");
            }
            calendar.Append("]");
            sb.AppendLine(calendar.ToString());
        }
        return sb.ToString().TrimEnd();
    }

    // ── Goals ──────────────────────────────────────────────────

    [KernelFunction, Description("Add a goal with milestones")]
    public async Task<string> AddGoalAsync(
        [Description("Goal title")] string title,
        [Description("Goal description")] string description,
        [Description("Target date (ISO 8601)")] string targetDate = "",
        [Description("Category: career, health, finance, personal, learning")] string category = "personal")
    {
        var goals = await LoadJsonListAsync<GoalItem>("goals.json");
        var goal = new GoalItem
        {
            Id          = Guid.NewGuid().ToString("N")[..8],
            Title       = title,
            Description = description,
            TargetDate  = string.IsNullOrEmpty(targetDate) ? null : DateTimeOffset.Parse(targetDate),
            Category    = category,
            Progress    = 0,
            CreatedAt   = DateTimeOffset.UtcNow,
            Status      = "active"
        };
        goals.Add(goal);
        await SaveJsonListAsync("goals.json", goals);
        return $"🎯 Goal added: [{goal.Id}] {title} ({category})";
    }

    [KernelFunction, Description("Update goal progress percentage (0-100)")]
    public async Task<string> UpdateGoalProgressAsync(
        [Description("Goal ID")] string goalId,
        [Description("Progress percentage (0-100)")] int progress,
        [Description("Progress note")] string note = "")
    {
        var goals = await LoadJsonListAsync<GoalItem>("goals.json");
        var goal = goals.FirstOrDefault(g => g.Id == goalId);
        if (goal == null) return $"Goal '{goalId}' not found.";

        goal.Progress = Math.Clamp(progress, 0, 100);
        if (progress >= 100) goal.Status = "completed";
        if (!string.IsNullOrEmpty(note)) goal.Notes.Add($"{DateTime.Today:yyyy-MM-dd}: {note}");

        await SaveJsonListAsync("goals.json", goals);
        var bar = "[" + new string('█', progress / 10) + new string('░', 10 - progress / 10) + "]";
        return $"🎯 Goal updated: {goal.Title} {bar} {progress}%{(progress >= 100 ? " ✅ COMPLETED!" : "")}";
    }

    [KernelFunction, Description("List all goals with their progress")]
    public async Task<string> ListGoalsAsync(
        [Description("Filter by status: all, active, completed")] string status = "active")
    {
        var goals = await LoadJsonListAsync<GoalItem>("goals.json");
        var filtered = status == "all" ? goals : goals.Where(g => g.Status == status).ToList();

        if (filtered.Count == 0) return "No goals found.";

        var sb = new StringBuilder($"🎯 Goals ({filtered.Count}):\n\n");
        foreach (var g in filtered.OrderBy(g => g.TargetDate ?? DateTimeOffset.MaxValue))
        {
            var bar = "[" + new string('█', g.Progress / 10) + new string('░', 10 - g.Progress / 10) + "]";
            var due = g.TargetDate.HasValue ? $" | Due: {g.TargetDate:yyyy-MM-dd}" : "";
            sb.AppendLine($"  [{g.Id}] {g.Title} {bar} {g.Progress}%{due} [{g.Category}]");
        }
        return sb.ToString().TrimEnd();
    }

    // ── Pomodoro ───────────────────────────────────────────────

    [KernelFunction, Description("Log a completed Pomodoro session")]
    public async Task<string> LogPomodoroAsync(
        [Description("Task worked on")] string task,
        [Description("Duration in minutes")] int durationMinutes = 25,
        [Description("Notes about the session")] string notes = "")
    {
        var sessions = await LoadJsonListAsync<PomodoroSession>("pomodoros.json");
        sessions.Add(new PomodoroSession
        {
            Task = task, DurationMinutes = durationMinutes,
            Notes = notes, CompletedAt = DateTimeOffset.UtcNow
        });
        await SaveJsonListAsync("pomodoros.json", sessions);

        var todaySessions = sessions.Count(s => s.CompletedAt.Date == DateTime.Today);
        var todayMinutes  = sessions.Where(s => s.CompletedAt.Date == DateTime.Today).Sum(s => s.DurationMinutes);
        return $"🍅 Pomodoro logged: {task} ({durationMinutes} min)\nToday: {todaySessions} sessions, {todayMinutes} minutes focused.";
    }

    [KernelFunction, Description("Get Pomodoro statistics for today or this week")]
    public async Task<string> GetPomodoroStatsAsync(
        [Description("Period: today, week, month")] string period = "today")
    {
        var sessions = await LoadJsonListAsync<PomodoroSession>("pomodoros.json");
        var now = DateTimeOffset.UtcNow;
        var filtered = period switch
        {
            "week"  => sessions.Where(s => (now - s.CompletedAt).TotalDays < 7),
            "month" => sessions.Where(s => (now - s.CompletedAt).TotalDays < 30),
            _       => sessions.Where(s => s.CompletedAt.Date == DateTime.Today)
        };

        var list = filtered.ToList();
        if (list.Count == 0) return $"No Pomodoro sessions for {period}.";

        var totalMin  = list.Sum(s => s.DurationMinutes);
        var taskGroups = list.GroupBy(s => s.Task).OrderByDescending(g => g.Sum(s => s.DurationMinutes));

        var sb = new StringBuilder($"🍅 Pomodoro Stats ({period}):\n\n");
        sb.AppendLine($"Sessions  : {list.Count}");
        sb.AppendLine($"Total time: {totalMin} min ({totalMin / 60}h {totalMin % 60}m)");
        sb.AppendLine($"\nTop tasks:");
        foreach (var g in taskGroups.Take(5))
            sb.AppendLine($"  {g.Sum(s => s.DurationMinutes),4} min | {g.Key}");
        return sb.ToString().TrimEnd();
    }

    // ── Private helpers ────────────────────────────────────────
    private string FilePath(string name) => Path.Combine(_dataDir, name);

    private async Task<List<T>> LoadJsonListAsync<T>(string filename)
    {
        var path = FilePath(filename);
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<T>>(json, _json) ?? [];
    }

    private async Task SaveJsonListAsync<T>(string filename, List<T> data)
    {
        var path = FilePath(filename);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _json));
    }

    private static DateTimeOffset? ParseNaturalDate(string input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        var lower = input.ToLower().Trim();
        if (lower == "today") return DateTime.Today;
        if (lower == "tomorrow") return DateTime.Today.AddDays(1);
        if (lower == "yesterday") return DateTime.Today.AddDays(-1);
        if (lower == "next week") return DateTime.Today.AddDays(7);
        if (Enum.TryParse<DayOfWeek>(lower, true, out var dow))
        {
            var d = DateTime.Today.AddDays(1);
            while (d.DayOfWeek != dow) d = d.AddDays(1);
            return d;
        }
        return DateTimeOffset.TryParse(input, out var dt) ? dt : null;
    }

    private static int ComputeStreak(List<HabitEntry> entries, string habitName)
    {
        int streak = 0;
        var date = DateTime.Today;
        while (true)
        {
            if (entries.Any(e => e.HabitName.Equals(habitName, StringComparison.OrdinalIgnoreCase) && e.Date.Date == date))
            { streak++; date = date.AddDays(-1); }
            else break;
        }
        return streak;
    }
}

// ── Data Models ─────────────────────────────────────────────
public class TaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "todo";
    public string Priority { get; set; } = "medium";
    public DateTimeOffset? DueDate { get; set; }
    public List<string> Tags { get; set; } = [];
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class NoteItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Type { get; set; } = "note";
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class HabitDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Frequency { get; set; } = "daily";
    public int Target { get; set; } = 1;
    public string Goal { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class HabitEntry
{
    public string HabitName { get; set; } = "";
    public DateTime Date { get; set; }
    public int Count { get; set; } = 1;
    public string Note { get; set; } = "";
}

public class GoalItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "personal";
    public DateTimeOffset? TargetDate { get; set; }
    public int Progress { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> Notes { get; set; } = [];
}

public class PomodoroSession
{
    public string Task { get; set; } = "";
    public int DurationMinutes { get; set; } = 25;
    public string Notes { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
}
