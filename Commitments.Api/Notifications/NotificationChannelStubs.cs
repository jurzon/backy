using System.Text;

namespace Commitments.Api.Notifications;

// Additional channel stubs (email / push). These are no-op / console log implementations
// Register explicitly in Program.cs if you want to route through them instead of / in addition to ConsoleNotificationSender.

public class EmailNotificationSender : INotificationSender
{
    public Task SendAsync(Guid userId, string channel, string subject, string body, CancellationToken ct = default)
    {
        // In a real implementation you would enqueue to an email service (SMTP, SendGrid, etc.)
        Console.WriteLine($"[EMAIL] user={userId} subj={subject} body={Trim(body)}");
        return Task.CompletedTask;
    }

    private static string Trim(string s) => s.Length <= 160 ? s : s.Substring(0,157) + "...";
}

public class PushNotificationSender : INotificationSender
{
    public Task SendAsync(Guid userId, string channel, string subject, string body, CancellationToken ct = default)
    {
        // Real implementation would integrate with APNs / FCM using stored device tokens.
        Console.WriteLine($"[PUSH] user={userId} title={subject} msg={Truncate(body, 80)}");
        return Task.CompletedTask;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max-3)] + "...";
}
