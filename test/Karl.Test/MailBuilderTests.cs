using Karl.Models;

namespace Karl.Test;

public class MailBuilderTests
{
    [Fact]
    public void Build_WhenPopulated_ContainsConfiguredValues()
    {
        var message = new MailBuilder()
            .From("sender@example.com", "Sender")
            .To("to@example.com", "To User")
            .Cc("cc@example.com")
            .Bcc("bcc@example.com")
            .Subject("Subject line")
            .TextBody("plain text")
            .HtmlBody("<p>html</p>")
            .Build();

        Assert.Equal("Sender <sender@example.com>", message.From.ToString());
        Assert.Single(message.To);
        Assert.Single(message.Cc);
        Assert.Single(message.Bcc);
        Assert.Equal("Subject line", message.Subject);
        Assert.Equal("plain text", message.Body.Text);
        Assert.Equal("<p>html</p>", message.Body.Html);
    }

    [Fact]
    public void TextBody_DoesNotOverwriteHtmlBody()
    {
        var message = new MailBuilder()
            .HtmlBody("<strong>html</strong>")
            .TextBody("plain")
            .Build();

        Assert.Equal("plain", message.Body.Text);
        Assert.Equal("<strong>html</strong>", message.Body.Html);
    }

    [Fact]
    public void HtmlBody_DoesNotOverwriteTextBody()
    {
        var message = new MailBuilder()
            .TextBody("plain")
            .HtmlBody("<strong>html</strong>")
            .Build();

        Assert.Equal("plain", message.Body.Text);
        Assert.Equal("<strong>html</strong>", message.Body.Html);
    }
}
