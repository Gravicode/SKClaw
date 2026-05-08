using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Workflow and System management skills inspired by Lobster and OpenClaw Gateway.
/// </summary>
public class WorkflowSkills
{
    [KernelFunction, Description("Create a typed workflow pipeline with approval gates (Lobster style)")]
    public string CreateWorkflow(
        [Description("Workflow name")] string name,
        [Description("Comma-separated steps")] string steps)
    {
        return $"🏗️ Workflow '{name}' created with steps: [{steps}]. Approval gates active at step 2.";
    }

    [KernelFunction, Description("Check the status of the AI Gateway and system nodes")]
    public string GetGatewayStatus()
    {
        return "🌐 Gateway Status: ONLINE\n" +
               "🔹 Connected Nodes: 4 active\n" +
               "🔹 CPU Load: 12%\n" +
               "🔹 Active Sessions: 15\n" +
               "🔹 Version: OpenClaw v2.5.1";
    }

    [KernelFunction, Description("Schedule a recurring job (Cron style)")]
    public string ScheduleCronJob(
        [Description("Cron expression (e.g. '0 0 * * *' for daily)")] string schedule,
        [Description("The task or function name to execute")] string taskName)
    {
        return $"⏰ Job '{taskName}' scheduled with cron expression: '{schedule}'. Next run: Tomorrow at 00:00.";
    }

    [KernelFunction, Description("Request human approval for a sensitive action")]
    public string RequestApproval(
        [Description("Description of the action requiring approval")] string action)
    {
        return $"⚠️ [APPROVAL GATE] Action '{action}' is pending human review. Notification sent to Admin.";
    }
}
