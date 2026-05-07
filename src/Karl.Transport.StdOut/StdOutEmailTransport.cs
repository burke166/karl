using System.Text;
using Karl.Models;

namespace Karl.Transport.StdOut;

public class StdOutEmailTransport : IEmailTransport
{
    public StdOutEmailTransport()
    {
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sb = new StringBuilder();
        sb.AppendLine($"From: {message.From}");
        sb.AppendLine($"To: {string.Join(", ", message.To)}");
        if (message.Cc.Count > 0)
            sb.AppendLine($"Cc: {string.Join(", ", message.Cc)}");
        if (message.Bcc.Count > 0)
            sb.AppendLine($"Bcc: {string.Join(", ", message.Bcc)}");
        sb.AppendLine($"Subject: {message.Subject}");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(message.Body.Text))
        {
            sb.AppendLine("=== TEXT ===");
            sb.AppendLine(message.Body.Text);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(message.Body.Html))
        {
            sb.AppendLine("=== HTML ===");
            sb.AppendLine(message.Body.Html);
        }

        Console.WriteLine(sb.ToString());
        return Task.CompletedTask;
    }
}
