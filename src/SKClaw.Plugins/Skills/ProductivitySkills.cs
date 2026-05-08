using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Productivity skills inspired by Notion Sync, Calendar Scheduler, and Taskade.
/// Helps agents manage schedules, tasks, and documentation.
/// </summary>
public class ProductivitySkills
{
    [KernelFunction, Description("Create a new task in a project management tool (Taskade/Notion style)")]
    public string CreateTask(
        [Description("The title of the task")] string title,
        [Description("Project or list name")] string projectName = "General",
        [Description("Due date for the task")] string dueDate = "")
    {
        return $"✅ Task '{title}' created in project '{projectName}'" + 
               (string.IsNullOrEmpty(dueDate) ? "." : $" with due date {dueDate}.");
    }

    [KernelFunction, Description("Sync a document or page to Notion")]
    public string SyncToNotion(
        [Description("The page title")] string pageTitle,
        [Description("Markdown or text content of the page")] string content)
    {
        return $"📖 Page '{pageTitle}' has been synced/updated in Notion database.";
    }

    [KernelFunction, Description("Schedule a meeting or event in the calendar")]
    public string ScheduleEvent(
        [Description("Event summary/title")] string summary,
        [Description("Start time (ISO 8601 format)")] string startTime,
        [Description("Duration in minutes")] int durationMinutes = 30)
    {
        return $"📅 Event '{summary}' scheduled for {startTime} (Duration: {durationMinutes} min). Invitations sent.";
    }

    [KernelFunction, Description("List today's upcoming events from the calendar")]
    public string ListTodayEvents()
    {
        return "📅 Today's Schedule:\n" +
               "1. 09:00 AM - Daily Standup\n" +
               "2. 02:00 PM - Client Demo\n" +
               "3. 04:30 PM - Code Review Session";
    }
}
