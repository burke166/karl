using Karl.Models;

namespace Karl;

public class MailBuilder
{
    private readonly EmailMessage _message = new();

    public MailBuilder From(string address, string? name = null)
    {
        _message.From = new EmailAddress(address, name);
        return this;
    }

    public MailBuilder To(string address, string? name = null)
    {
        _message.To.Add(new EmailAddress(address, name));
        return this;
    }

    public MailBuilder Cc(string address, string? name = null)
    {
        _message.Cc.Add(new EmailAddress(address, name));
        return this;
    }

    public MailBuilder Bcc(string address, string? name = null)
    {
        _message.Bcc.Add(new EmailAddress(address, name));
        return this;
    }

    public MailBuilder Subject(string subject)
    {
        _message.Subject = subject;
        return this;
    }

    public MailBuilder TextBody(string text)
    {
        _message.Body = new EmailBody { Text = text, Html = _message.Body.Html };
        return this;
    }

    public MailBuilder HtmlBody(string html)
    {
        _message.Body = new EmailBody { Text = _message.Body.Text, Html = html };
        return this;
    }

    public EmailMessage Build() => _message;
}
