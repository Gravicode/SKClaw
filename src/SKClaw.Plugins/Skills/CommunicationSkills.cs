using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Skills;

/// <summary>
/// Communication skill inspired by Agent Mail and Matrix.
/// Handles emails and real-time messaging.
/// </summary>
public class CommunicationSkills
{
    [KernelFunction, Description("Send an email to a recipient (Agent Mail style)")]
    public string SendEmail(
        [Description("Recipient email address")] string to,
        [Description("Email subject")] string subject,
        [Description("Email body content")] string body)
    {
        // Simulation: Integration with SMTP or SendGrid/Mailgun API would go here.
        return $"📧 Email sent successfully to {to}.\nSubject: {subject}\n[Log]: Status 200 OK";
    }

    [KernelFunction, Description("Retrieve latest unread emails from the inbox")]
    public string GetUnreadEmails(
        [Description("Limit of emails to fetch")] int limit = 3)
    {
        // Simulation
        return $"You have 2 unread emails:\n" +
               "1. From: boss@company.com - Subject: Project Deadline Update\n" +
               "2. From: security@alert.com - Subject: New Login Detected";
    }

    [KernelFunction, Description("Send a message to a Matrix room or DM")]
    public string SendMatrixMessage(
        [Description("Room ID or User ID")] string target,
        [Description("Message content")] string message)
    {
        // Simulation: Matrix API integration
        return $"💬 Message sent to Matrix [{target}]: {message}";
    }

    [KernelFunction, Description("Create a new collaboration room for a project")]
    public string CreateCollaborationRoom(
        [Description("Name of the room")] string roomName)
    {
        return $"🏢 Collaboration room '#{roomName.ToLower().Replace(" ", "_")}:matrix.org' has been created.";
    }
}
