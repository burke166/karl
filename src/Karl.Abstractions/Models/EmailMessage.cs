namespace Karl.Models;

public sealed class EmailMessage
{
    public EmailAddress From { get; set; } = default!;
    public List<EmailAddress> To { get; } = new();
    public List<EmailAddress> Cc { get; } = new();
    public List<EmailAddress> Bcc { get; } = new();

    public string Subject { get; set; } = string.Empty;

    public EmailBody Body { get; set; } = new();

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();
}
