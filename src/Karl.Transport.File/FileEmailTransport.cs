using System.Text;
using Karl.Models;

namespace Karl.Transport.File;

public class FileEmailTransport : IEmailTransport
{
    private readonly FileTransportOptions _options;

    public FileEmailTransport(FileTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.DirectoryPath);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"{_options.FileNamePrefix}_{timestamp}.txt";
        var fullPath = Path.Combine(_options.DirectoryPath, fileName);

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

        await System.IO.File.WriteAllTextAsync(fullPath, sb.ToString(), cancellationToken);
    }
}
