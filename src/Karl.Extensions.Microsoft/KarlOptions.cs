using Karl.Transport.File;
using Karl.Transport.Smtp;

namespace Karl.Extensions.Microsoft;

public sealed class KarlOptions
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool IsHtml { get; set; } = true;
    public string? Transport { get; set; } = "smtp";
    public SmtpTransportOptions Smtp { get; set; } = new();
    public FileTransportOptions File { get; set; } = new();
}