using System.IO;
using Karl.Models;
using Karl.Transport.StdOut;

namespace Karl.Test;

public sealed class StdOutEmailTransportTests
{
    [Fact]
    public async Task SendAsync_WritesFromToAndSubject()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.Contains("From: sender@example.com", output);
        Assert.Contains("To: recipient@example.com", output);
        Assert.Contains("Subject: Test Subject", output);
    }

    [Fact]
    public async Task SendAsync_WithMultipleRecipients_JoinsRecipientsWithCommaSpace()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.To.Clear();
        message.To.Add(new EmailAddress("first@example.com"));
        message.To.Add(new EmailAddress("second@example.com"));

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.Contains("To: first@example.com, second@example.com", output);
    }

    [Fact]
    public async Task SendAsync_WithCc_WritesCcLine()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Cc.Add(new EmailAddress("copy@example.com"));

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.Contains("Cc: copy@example.com", output);
    }

    [Fact]
    public async Task SendAsync_WithoutCc_DoesNotWriteCcLine()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.DoesNotContain("Cc:", output);
    }

    [Fact]
    public async Task SendAsync_WithBcc_WritesBccLine()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Bcc.Add(new EmailAddress("blind@example.com"));

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.Contains("Bcc: blind@example.com", output);
    }

    [Fact]
    public async Task SendAsync_WithoutBcc_DoesNotWriteBccLine()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.DoesNotContain("Bcc:", output);
    }

    [Fact]
    public async Task SendAsync_WithTextBody_WritesTextSection()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Body = new EmailBody { Text = "Plain text body", Html = null };

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.Contains("=== TEXT ===", output);
        Assert.Contains("Plain text body", output);
    }

    [Fact]
    public async Task SendAsync_WithoutTextBody_DoesNotWriteTextSection()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Body = new EmailBody { Text = string.Empty, Html = "<p>html only</p>" };

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.DoesNotContain("=== TEXT ===", output);
    }

    [Fact]
    public async Task SendAsync_WithHtmlBody_WritesHtmlSection()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Body = new EmailBody { Text = null, Html = "<p>Html body</p>" };

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.Contains("=== HTML ===", output);
        Assert.Contains("<p>Html body</p>", output);
    }

    [Fact]
    public async Task SendAsync_WithoutHtmlBody_DoesNotWriteHtmlSection()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Body = new EmailBody { Text = "text only", Html = string.Empty };

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        Assert.DoesNotContain("=== HTML ===", output);
    }

    [Fact]
    public async Task SendAsync_WithTextAndHtml_WritesBothSectionsInOrder()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();
        message.Body = new EmailBody { Text = "Text content", Html = "<p>Html content</p>" };

        var output = await CaptureOutputAsync(() => transport.SendAsync(message));

        var textIndex = output.IndexOf("=== TEXT ===", StringComparison.Ordinal);
        var htmlIndex = output.IndexOf("=== HTML ===", StringComparison.Ordinal);
        Assert.True(textIndex >= 0, "Expected text section marker.");
        Assert.True(htmlIndex >= 0, "Expected HTML section marker.");
        Assert.True(textIndex < htmlIndex, "Expected text section before HTML section.");
    }

    [Fact]
    public async Task SendAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var transport = new StdOutEmailTransport();

        await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!));
    }

    [Fact]
    public async Task SendAsync_ReturnsCompletedTask()
    {
        var transport = new StdOutEmailTransport();
        var message = CreateBaseMessage();

        await transport.SendAsync(message);
    }

    private static EmailMessage CreateBaseMessage()
    {
        var message = new EmailMessage
        {
            From = new EmailAddress("sender@example.com"),
            Subject = "Test Subject",
            Body = new EmailBody()
        };

        message.To.Add(new EmailAddress("recipient@example.com"));
        return message;
    }

    private static async Task<string> CaptureOutputAsync(Func<Task> action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
