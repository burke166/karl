using Karl.Models;
using Karl.Transport.File;

namespace Karl.Test;

public class FileEmailTransportTests
{
    [Fact]
    public async Task SendAsync_WritesMessageToConfiguredDirectory()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var sut = CreateSut(tempDirectory.Path);

        await sut.SendAsync(CreateMessage());

        var files = Directory.GetFiles(tempDirectory.Path);
        Assert.Single(files);
    }

    [Fact]
    public async Task SendAsync_WritesExpectedMessageContent()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var sut = CreateSut(tempDirectory.Path);
        var message = CreateMessage();

        await sut.SendAsync(message);

        var filePath = Assert.Single(Directory.GetFiles(tempDirectory.Path));
        var content = await File.ReadAllTextAsync(filePath);

        Assert.Contains("From: Sender <from@example.com>", content, StringComparison.Ordinal);
        Assert.Contains("To: Recipient <to@example.com>", content, StringComparison.Ordinal);
        Assert.Contains("Subject: test subject", content, StringComparison.Ordinal);
        Assert.Contains("=== TEXT ===", content, StringComparison.Ordinal);
        Assert.Contains("test text body", content, StringComparison.Ordinal);
        Assert.Contains("=== HTML ===", content, StringComparison.Ordinal);
        Assert.Contains("<p>test html body</p>", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_CreatesOutputDirectoryIfMissing()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var outputDirectory = System.IO.Path.Combine(tempDirectory.Path, "nested", "mail-out");
        var sut = CreateSut(outputDirectory);

        Assert.False(Directory.Exists(outputDirectory));

        await sut.SendAsync(CreateMessage());

        Assert.True(Directory.Exists(outputDirectory));
        Assert.Single(Directory.GetFiles(outputDirectory));
    }

    [Fact]
    public async Task SendAsync_UsesUniqueFileNames()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var sut = CreateSut(tempDirectory.Path);

        await sut.SendAsync(CreateMessage("first subject"));
        await Task.Delay(2);
        await sut.SendAsync(CreateMessage("second subject"));

        var files = Directory.GetFiles(tempDirectory.Path);
        Assert.Equal(2, files.Length);
        Assert.Equal(2, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var contents = await Task.WhenAll(files.Select(file => File.ReadAllTextAsync(file)));
        Assert.Contains(contents, text => text.Contains("Subject: first subject", StringComparison.Ordinal));
        Assert.Contains(contents, text => text.Contains("Subject: second subject", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var sut = CreateSut(tempDirectory.Path);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SendAsync(null!));
    }

    [Fact]
    public async Task SendAsync_WhenOutputDirectoryIsInvalid_ThrowsMeaningfulException()
    {
        var sut = CreateSut("\0invalid-path");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => sut.SendAsync(CreateMessage()));
        Assert.True(ex is ArgumentException or NotSupportedException);
    }

    [Fact]
    public async Task SendAsync_PassesCancellationToken()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var sut = CreateSut(tempDirectory.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.SendAsync(CreateMessage(), cts.Token));
    }

    private static FileEmailTransport CreateSut(string outputDirectory)
    {
        var options = new FileTransportOptions
        {
            DirectoryPath = outputDirectory,
            FileNamePrefix = "mail"
        };

        return new FileEmailTransport(options);
    }

    private static EmailMessage CreateMessage(string subject = "test subject")
    {
        var message = new EmailMessage
        {
            From = new EmailAddress("from@example.com", "Sender"),
            Subject = subject,
            Body = new EmailBody
            {
                Text = "test text body",
                Html = "<p>test html body</p>"
            }
        };

        message.To.Add(new EmailAddress("to@example.com", "Recipient"));
        return message;
    }
}
