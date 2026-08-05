using Karl.Cli;
using Karl.Models;
using Karl.Transport.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Karl.Test;

public class KarlCliCommandTests
{
    private static readonly Lock ConsoleLock = new();

    [Fact]
    public async Task Preview_Succeeds_WithRequiredFields()
    {
        var exitCode = await InvokeAsync(["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body"]);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task File_Succeeds_WithRequiredFields_AndCreatesOutputFile()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var outputDir = Path.Combine(tempDir, "out");
            var exitCode = await InvokeAsync(["file", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--output", outputDir]);

            Assert.Equal(0, exitCode);
            Assert.True(Directory.Exists(outputDir));
            Assert.NotEmpty(Directory.GetFiles(outputDir, "*.txt"));
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Theory]
    [InlineData("--to", "No to address was provided.")]
    [InlineData("--from", "No from address was provided.")]
    [InlineData("--subject", "No email subject was provided.")]
    public async Task Preview_MissingRequiredEmailField_ReturnsExitCode1(string missingField, string expectedError)
    {
        var args = new List<string> { "preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body" };
        var index = args.IndexOf(missingField);
        args.RemoveAt(index);
        args.RemoveAt(index);

        var output = new StringWriter();
        var exitCode = await InvokeAsync(args.ToArray(), new KarlCliCommandFactoryOptions
        {
            Write = output.Write,
            WriteLine = output.WriteLine
        });

        Assert.Equal(1, exitCode);
        Assert.Contains(expectedError, output.ToString());
    }

    [Fact]
    public async Task Preview_MissingBodyContent_ReturnsExitCode1()
    {
        var output = new StringWriter();
        var exitCode = await InvokeAsync(["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject"], new KarlCliCommandFactoryOptions
        {
            Write = output.Write,
            WriteLine = output.WriteLine
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("No content provided for email body.", output.ToString());
    }

    [Fact]
    public async Task Preview_UsesMarkdownFileContent_WhenPresent()
    {
        var tempDir = CreateTempDirectory();
        var markdownPath = Path.Combine(tempDir, "body.md");
        await File.WriteAllTextAsync(markdownPath, "Body from markdown");

        var capture = new CaptureSink();
        var exitCode = await InvokeAsync(
            ["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Inline body", "--markdown", markdownPath],
            CreateCaptureOptions(capture));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capture.LastMessage);
        Assert.Contains("Body from markdown", capture.LastMessage!.Body.Text);
        DeleteDirectory(tempDir);
    }

    [Fact]
    public async Task Preview_UsesInlineBody_WhenNoMarkdownFileProvided()
    {
        var capture = new CaptureSink();
        var exitCode = await InvokeAsync(
            ["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Inline body"],
            CreateCaptureOptions(capture));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capture.LastMessage);
        Assert.Contains("Inline body", capture.LastMessage!.Body.Text);
    }

    [Fact]
    public async Task Preview_MarkdownTakesPrecedenceOverBody()
    {
        var tempDir = CreateTempDirectory();
        var markdownPath = Path.Combine(tempDir, "body.md");
        await File.WriteAllTextAsync(markdownPath, "Markdown wins");

        try
        {
            var capture = new CaptureSink();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Inline body", "--markdown", markdownPath],
                CreateCaptureOptions(capture));

            Assert.Equal(0, exitCode);
            Assert.NotNull(capture.LastMessage);
            Assert.Contains("Markdown wins", capture.LastMessage!.Body.Text);
            Assert.DoesNotContain("Inline body", capture.LastMessage.Body.Text);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_ModelJson_IsLoadedAndUsedForRendering()
    {
        var tempDir = CreateTempDirectory();
        var modelPath = Path.Combine(tempDir, "model.json");
        await File.WriteAllTextAsync(modelPath, """{"name":"Chris"}""");

        try
        {
            var capture = new CaptureSink();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Hi {{name}}", "--body", "Body for {{name}}", "--model", modelPath],
                CreateCaptureOptions(capture));

            Assert.Equal(0, exitCode);
            Assert.NotNull(capture.LastMessage);
            Assert.Equal("Hi Chris", capture.LastMessage!.Subject.Trim());
            Assert.Contains("Body for Chris", capture.LastMessage.Body.Text);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_Verbose_WritesExpectedLines()
    {
        var output = new StringWriter();
        var exitCode = await InvokeAsync(
            ["preview", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--verbose"],
            new KarlCliCommandFactoryOptions
            {
                Write = output.Write,
                WriteLine = output.WriteLine
            });

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("Karl CLI starting...", text);
        Assert.Contains("Sending email...", text);
        Assert.Contains("Done.", text);
    }

    [Fact]
    public async Task Send_MapsSmtpOptionsCorrectly()
    {
        var capture = new CaptureSink();
        var options = CreateCaptureOptions(capture);

        var exitCode = await InvokeAsync(
            ["send", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--smtp-host", "smtp.example.com", "--smtp-port", "2525", "--username", "user", "--password", "pass", "--tls", "StartTlsWhenAvailable"],
            options);

        Assert.Equal(0, exitCode);
        Assert.NotNull(capture.LastSmtpOptions);
        Assert.Equal("smtp.example.com", capture.LastSmtpOptions!.Host);
        Assert.Equal(2525, capture.LastSmtpOptions.Port);
        Assert.Equal("user", capture.LastSmtpOptions.Username);
        Assert.Equal("pass", capture.LastSmtpOptions.Password);
        Assert.Equal("StartTlsWhenAvailable", capture.LastSmtpOptions.SecurityMode);
    }

    [Fact]
    public async Task Send_DefaultsPort_WhenNotProvided()
    {
        var capture = new CaptureSink();
        var exitCode = await InvokeAsync(
            ["send", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--smtp-host", "smtp.example.com"],
            CreateCaptureOptions(capture));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capture.LastSmtpOptions);
        Assert.Equal(587, capture.LastSmtpOptions!.Port);
    }

    [Fact]
    public async Task Send_DefaultsTlsMode_WhenNotProvided()
    {
        var capture = new CaptureSink();
        var exitCode = await InvokeAsync(
            ["send", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--smtp-host", "smtp.example.com"],
            CreateCaptureOptions(capture));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capture.LastSmtpOptions);
        Assert.Equal("StartTlsRequired", capture.LastSmtpOptions!.SecurityMode);
    }

    [Fact]
    public async Task File_DefaultsOutputDirectoryToEmails_WhenOutputNotProvided()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var exitCode = await InvokeAsync(
                ["file", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body"],
                currentDirectory: tempDir);

            Assert.Equal(0, exitCode);
            var outputDir = Path.Combine(tempDir, "emails");
            Assert.True(Directory.Exists(outputDir));
            Assert.NotEmpty(Directory.GetFiles(outputDir, "*.txt"));
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_Csv_SendsOneEmailPerRow_WithTokensSubstituted()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\nalice@example.com,Alice\r\nbob@example.com,Bob\r\n");

            var capture = new CaptureSink();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--subject", "Hi {{FirstName}}", "--body", "Body for {{FirstName}}", "--csv", csvPath, "--to-column", "Email"],
                CreateCaptureOptions(capture));

            Assert.Equal(0, exitCode);
            Assert.Equal(2, capture.SentMessages.Count);
            Assert.Equal("alice@example.com", capture.SentMessages[0].To[0].Address);
            Assert.Equal("Hi Alice", capture.SentMessages[0].Subject.Trim());
            Assert.Equal("bob@example.com", capture.SentMessages[1].To[0].Address);
            Assert.Equal("Hi Bob", capture.SentMessages[1].Subject.Trim());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_Csv_MissingToColumnHeader_ReturnsExitCode1WithClearError()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\nalice@example.com,Alice\r\n");

            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--subject", "Subject", "--body", "Body", "--csv", csvPath, "--to-column", "NotAColumn"],
                new KarlCliCommandFactoryOptions { Write = output.Write, WriteLine = output.WriteLine });

            Assert.Equal(1, exitCode);
            Assert.Contains("NotAColumn", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_Csv_HeaderOnlyZeroRows_ReturnsExitCode0WithZeroSentSummary()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\n");

            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--subject", "Subject", "--body", "Body", "--csv", csvPath, "--to-column", "Email"],
                new KarlCliCommandFactoryOptions { Write = output.Write, WriteLine = output.WriteLine });

            Assert.Equal(0, exitCode);
            Assert.Contains("Sent 0 of 0 emails", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_Csv_SkipsRowsWithBlankRecipient_AndReportsSkippedCount()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\nalice@example.com,Alice\r\n,Bob\r\n");

            var capture = new CaptureSink();
            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--subject", "Subject", "--body", "Body", "--csv", csvPath, "--to-column", "Email"],
                CreateCaptureOptions(capture, output));

            Assert.Equal(1, exitCode);
            Assert.Single(capture.SentMessages);
            Assert.Contains("Sent 1 of 2 emails", output.ToString());
            Assert.Contains("1 skipped", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_CsvAndModel_ReturnsExitCode1()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email\r\nalice@example.com\r\n");
            var modelPath = Path.Combine(tempDir, "model.json");
            await File.WriteAllTextAsync(modelPath, "{}");

            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--subject", "Subject", "--body", "Body", "--csv", csvPath, "--to-column", "Email", "--model", modelPath],
                new KarlCliCommandFactoryOptions { Write = output.Write, WriteLine = output.WriteLine });

            Assert.Equal(1, exitCode);
            Assert.Contains("--csv and --model cannot be combined", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_CsvAndTo_ReturnsExitCode1()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email\r\nalice@example.com\r\n");

            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--to", "someone@example.com", "--subject", "Subject", "--body", "Body", "--csv", csvPath, "--to-column", "Email"],
                new KarlCliCommandFactoryOptions { Write = output.Write, WriteLine = output.WriteLine });

            Assert.Equal(1, exitCode);
            Assert.Contains("--to is not used in CSV batch mode", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task File_Csv_CreatesOneFileForEachRow()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\nalice@example.com,Alice\r\nbob@example.com,Bob\r\n");
            var outputDir = Path.Combine(tempDir, "out");

            var exitCode = await InvokeAsync(
                ["file", "--from", "from@example.com", "--subject", "Hi {{FirstName}}", "--body", "Body for {{FirstName}}", "--csv", csvPath, "--to-column", "Email", "--output", outputDir]);

            Assert.Equal(0, exitCode);
            Assert.True(Directory.Exists(outputDir));
            Assert.Equal(2, Directory.GetFiles(outputDir, "*.txt").Length);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Send_Csv_ContinuesAfterOneRowFails_AndReturnsExitCode1()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\nalice@example.com,Alice\r\nbob@example.com,Bob\r\ncarol@example.com,Carol\r\n");

            var capture = new CaptureSink { FailOnToAddress = "bob@example.com" };
            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["send", "--from", "from@example.com", "--subject", "Hi {{FirstName}}", "--body", "Body for {{FirstName}}", "--csv", csvPath, "--to-column", "Email", "--smtp-host", "smtp.example.com"],
                CreateCaptureOptions(capture, output));

            Assert.Equal(1, exitCode);
            Assert.Equal(2, capture.SentMessages.Count);
            Assert.Contains("Sent 2 of 3 emails", output.ToString());
            Assert.Contains("1 failed", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Send_WithAttach_IncludesAttachmentInSentMessage()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var attachPath = Path.Combine(tempDir, "invoice.pdf");
            await File.WriteAllTextAsync(attachPath, "invoice content");

            var capture = new CaptureSink();
            var exitCode = await InvokeAsync(
                ["send", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--smtp-host", "smtp.example.com", "--attach", attachPath],
                CreateCaptureOptions(capture));

            Assert.Equal(0, exitCode);
            Assert.NotNull(capture.LastMessage);
            var attachment = Assert.Single(capture.LastMessage!.Attachments);
            Assert.Equal("invoice.pdf", attachment.FileName);
            Assert.Equal("application/pdf", attachment.ContentType);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Send_WithMultipleAttach_IncludesAllAttachments()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var firstPath = Path.Combine(tempDir, "invoice.pdf");
            var secondPath = Path.Combine(tempDir, "terms.txt");
            await File.WriteAllTextAsync(firstPath, "invoice content");
            await File.WriteAllTextAsync(secondPath, "terms content");

            var capture = new CaptureSink();
            var exitCode = await InvokeAsync(
                ["send", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--smtp-host", "smtp.example.com", "--attach", firstPath, "-a", secondPath],
                CreateCaptureOptions(capture));

            Assert.Equal(0, exitCode);
            Assert.NotNull(capture.LastMessage);
            Assert.Equal(2, capture.LastMessage!.Attachments.Count);
            Assert.Contains(capture.LastMessage.Attachments, a => a.FileName == "invoice.pdf");
            Assert.Contains(capture.LastMessage.Attachments, a => a.FileName == "terms.txt");
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Send_WithMissingAttachFile_ReturnsExitCode1WithClearError()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var missingPath = Path.Combine(tempDir, "does-not-exist.pdf");

            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["send", "--from", "from@example.com", "--to", "to@example.com", "--subject", "Subject", "--body", "Body", "--smtp-host", "smtp.example.com", "--attach", missingPath],
                new KarlCliCommandFactoryOptions { Write = output.Write, WriteLine = output.WriteLine });

            Assert.Equal(1, exitCode);
            Assert.Contains("Attachment file not found", output.ToString());
            Assert.Contains(missingPath, output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Send_CsvWithAttach_AppliesSameAttachmentsToEveryRow()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var csvPath = Path.Combine(tempDir, "contacts.csv");
            await File.WriteAllTextAsync(csvPath, "Email,FirstName\r\nalice@example.com,Alice\r\nbob@example.com,Bob\r\n");
            var attachPath = Path.Combine(tempDir, "welcome-packet.pdf");
            await File.WriteAllTextAsync(attachPath, "packet content");

            var capture = new CaptureSink();
            var exitCode = await InvokeAsync(
                ["send", "--from", "from@example.com", "--subject", "Hi {{FirstName}}", "--body", "Body for {{FirstName}}", "--csv", csvPath, "--to-column", "Email", "--smtp-host", "smtp.example.com", "--attach", attachPath],
                CreateCaptureOptions(capture));

            Assert.Equal(0, exitCode);
            Assert.Equal(2, capture.SentMessages.Count);
            Assert.All(capture.SentMessages, message =>
            {
                var attachment = Assert.Single(message.Attachments);
                Assert.Equal("welcome-packet.pdf", attachment.FileName);
            });
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Preview_Csv_MissingFile_ReturnsExitCode1WithClearError()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var missingPath = Path.Combine(tempDir, "does-not-exist.csv");

            var output = new StringWriter();
            var exitCode = await InvokeAsync(
                ["preview", "--from", "from@example.com", "--subject", "Subject", "--body", "Body", "--csv", missingPath, "--to-column", "Email"],
                new KarlCliCommandFactoryOptions { Write = output.Write, WriteLine = output.WriteLine });

            Assert.Equal(1, exitCode);
            Assert.Contains("Could not read CSV file", output.ToString());
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RootHelp_ShowsExpectedCommands()
    {
        var output = await InvokeWithConsoleCaptureAsync(["--help"]);
        Assert.Contains("send", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preview", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("preview", "--from", "--to", "--subject", "--body", "--markdown", "--model", "--json", "--layout", "--layout-data", "--css", "--csv", "--to-column", "--name-column", "--attach", "--verbose")]
    [InlineData("file", "--from", "--to", "--subject", "--body", "--markdown", "--model", "--json", "--layout", "--layout-data", "--css", "--csv", "--to-column", "--name-column", "--attach", "--verbose", "--output")]
    [InlineData("send", "--from", "--to", "--subject", "--body", "--markdown", "--model", "--json", "--layout", "--layout-data", "--css", "--csv", "--to-column", "--name-column", "--attach", "--verbose", "--smtp-host", "--smtp-port", "--username", "--password", "-tls")]
    public async Task CommandHelp_ShowsExpectedOptions(string commandName, params string[] expectedOptions)
    {
        var output = await InvokeWithConsoleCaptureAsync([commandName, "--help"]);

        foreach (var expectedOption in expectedOptions)
        {
            Assert.Contains(expectedOption, output, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static KarlCliCommandFactoryOptions CreateCaptureOptions(CaptureSink capture, StringWriter? output = null)
    {
        return new KarlCliCommandFactoryOptions
        {
            ConfigureServices = services =>
            {
                services.AddSingleton(capture);
                services.AddSingleton<IEmailService, CapturingEmailService>();
            },
            Write = output is null ? null : output.Write,
            WriteLine = output is null ? null : output.WriteLine
        };
    }

    private static async Task<int> InvokeAsync(string[] args, KarlCliCommandFactoryOptions? options = null, string? currentDirectory = null)
    {
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            if (!string.IsNullOrWhiteSpace(currentDirectory))
            {
                Environment.CurrentDirectory = currentDirectory;
            }

            var root = KarlCliCommandFactory.CreateRootCommand(options);
            var result = root.Parse(args);
            return await result.InvokeAsync();
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private static Task<string> InvokeWithConsoleCaptureAsync(string[] args)
    {
        lock (ConsoleLock)
        {
            var writer = new StringWriter();
            var originalOut = Console.Out;
            try
            {
                Console.SetOut(writer);
                var root = KarlCliCommandFactory.CreateRootCommand();
                var parseResult = root.Parse(args);
                parseResult.InvokeAsync().GetAwaiter().GetResult();
                return Task.FromResult(writer.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "karl-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class CaptureSink
    {
        public EmailMessage? LastMessage { get; set; }
        public SmtpTransportOptions? LastSmtpOptions { get; set; }
        public List<EmailMessage> SentMessages { get; } = new();
        public string? FailOnToAddress { get; set; }
    }

    private sealed class CapturingEmailService : IEmailService
    {
        private readonly CaptureSink _capture;
        private readonly IServiceProvider _serviceProvider;

        public CapturingEmailService(CaptureSink capture, IServiceProvider serviceProvider)
        {
            _capture = capture;
            _serviceProvider = serviceProvider;
        }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (_capture.FailOnToAddress is not null && message.To.Any(address => address.Address == _capture.FailOnToAddress))
            {
                throw new InvalidOperationException($"Simulated failure for {_capture.FailOnToAddress}");
            }

            _capture.LastMessage = message;
            _capture.SentMessages.Add(message);
            var smtpOptions = _serviceProvider.GetService<IOptions<SmtpTransportOptions>>();
            if (smtpOptions is not null)
            {
                _capture.LastSmtpOptions = new SmtpTransportOptions
                {
                    Host = smtpOptions.Value.Host,
                    Port = smtpOptions.Value.Port,
                    Username = smtpOptions.Value.Username,
                    Password = smtpOptions.Value.Password,
                    SecurityMode = smtpOptions.Value.SecurityMode
                };
            }
            return Task.CompletedTask;
        }
    }
}
