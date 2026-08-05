using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using ComputerCodeBlue.Csv;
using Karl.Extensions.Microsoft;
using Karl.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Karl.Cli;

public sealed class KarlCliCommandFactoryOptions
{
    public Action<IServiceCollection>? ConfigureServices { get; init; }
    public Func<string?, IConfigurationRoot>? LoadConfiguration { get; init; }
    public Action<string>? Write { get; init; }
    public Action<string>? WriteLine { get; init; }
    public Func<string, bool>? FileExists { get; init; }
    public Func<string, string>? ReadAllText { get; init; }
}

public static class KarlCliCommandFactory
{
    public static RootCommand CreateRootCommand(KarlCliCommandFactoryOptions? factoryOptions = null)
    {
        var write = factoryOptions?.Write ?? Console.Write;
        var writeLine = factoryOptions?.WriteLine ?? Console.WriteLine;
        var loadConfiguration = factoryOptions?.LoadConfiguration ?? KarlCliConfiguration.Load;
        var fileExists = factoryOptions?.FileExists ?? File.Exists;
        var readAllText = factoryOptions?.ReadAllText ?? File.ReadAllText;

        var root = new RootCommand("Karl CLI - The Mailman Delivers");
        var send = new Command("send", "Sends email using SMTP transport.");
        var file = new Command("file", "Outputs to a file instead of sending email. Useful for diagnostics.");
        var preview = new Command("preview", "Outputs to stdout instead of sending email. Useful for diagnostics.");

        var verbose = new Option<bool>("--verbose", ["-v"])
        {
            Description = "Enable verbose output",
            Required = false
        };

        var from = new Option<string>("--from", ["-f"])
        {
            Description = "From email",
            Required = false
        };

        var to = new Option<string>("--to", ["-t"])
        {
            Description = "To email",
            Required = false
        };

        var jsonPath = new Option<string>("--json", ["-j"])
        {
            Description = "Path to JSON configuration",
            Required = false
        };

        var subject = new Option<string>("--subject", ["-s"])
        {
            Description = "Subject template",
            Required = false
        };

        var body = new Option<string?>("--body", ["-b"])
        {
            Description = "Email body template (Markdown)",
            Required = false
        };

        var markdownPath = new Option<string?>("--markdown", ["-md"])
        {
            Description = "Path to Markdown template",
            Required = false
        };

        var modelPath = new Option<string?>("--model", ["-m"])
        {
            Description = "Path to JSON model",
            Required = false
        };

        var layout = new Option<string>("--layout", ["-l"])
        {
            Description = "Layout key",
            Required = false,
            DefaultValueFactory = _ => "default"
        };

        var layoutDataPath = new Option<string?>("--layout-data", ["-ld"])
        {
            Description = "Path to JSON layout data",
            Required = false
        };

        var cssPath = new Option<string?>("--css", ["-c"])
        {
            Description = "Path to CSS to inline",
            Required = false
        };

        var csvPath = new Option<string?>("--csv")
        {
            Description = "Path to a CSV file for mass email; each row is one recipient. Switches the command into batch mode.",
            Required = false
        };

        var toColumn = new Option<string?>("--to-column")
        {
            Description = "Name of the CSV column holding the recipient email address. Required when --csv is provided.",
            Required = false
        };

        var nameColumn = new Option<string?>("--name-column")
        {
            Description = "Name of the CSV column holding the recipient's display name.",
            Required = false
        };

        var smtpHost = new Option<string>("--smtp-host", ["-h", "--host"])
        {
            Description = "SMTP host",
            Required = true
        };

        var smtpPort = new Option<int>("--smtp-port", ["-P", "--port"])
        {
            Description = "SMTP port",
            DefaultValueFactory = _ => 587,
            Required = false
        };

        var username = new Option<string?>("--username", ["-u"])
        {
            Description = "SMTP username",
            Required = false
        };

        var password = new Option<string?>("--password", ["-p"])
        {
            Description = "SMTP password",
            Required = false
        };

        var output = new Option<string>("--output", ["-o"])
        {
            Description = "Output directory for file transport",
            Required = false,
            DefaultValueFactory = _ => "emails"
        };

        var tls = new Option<string>("--tls", ["-tls"])
        {
            Description = "STARTTLS behavior",
            Required = false
        };

        var attach = new Option<string[]>("--attach", ["-a"])
        {
            Description = "Path to a local file to attach. Repeatable to attach multiple files.",
            Required = false,
            DefaultValueFactory = _ => Array.Empty<string>()
        };

        void AddCommonOptions(Command command)
        {
            command.Options.Add(verbose);
            command.Options.Add(from);
            command.Options.Add(to);
            command.Options.Add(subject);
            command.Options.Add(body);
            command.Options.Add(jsonPath);
            command.Options.Add(markdownPath);
            command.Options.Add(modelPath);
            command.Options.Add(layout);
            command.Options.Add(layoutDataPath);
            command.Options.Add(cssPath);
            command.Options.Add(csvPath);
            command.Options.Add(toColumn);
            command.Options.Add(nameColumn);
            command.Options.Add(attach);
        }

        AddCommonOptions(file);
        file.Options.Add(output);

        AddCommonOptions(send);
        send.Options.Add(smtpHost);
        send.Options.Add(smtpPort);
        send.Options.Add(username);
        send.Options.Add(password);
        send.Options.Add(tls);

        AddCommonOptions(preview);

        async Task<int> HandleEmailAsync(ParseResult parseResult, CancellationToken cancellationToken, Action<IKarlBuilder, ParseResult> configureKarlTransport)
        {
            var verboseValue = parseResult.GetValue(verbose);
            var fromValue = parseResult.GetValue(from);
            var toValue = parseResult.GetValue(to);
            var subjectValue = parseResult.GetValue(subject);
            var jsonPathValue = parseResult.GetValue(jsonPath);
            var markdownPathValue = parseResult.GetValue(markdownPath);
            var bodyValue = parseResult.GetValue(body);
            var modelPathValue = parseResult.GetValue(modelPath);
            var csvPathValue = parseResult.GetValue(csvPath);
            var toColumnValue = parseResult.GetValue(toColumn);
            var nameColumnValue = parseResult.GetValue(nameColumn);
            var attachValues = parseResult.GetValue(attach) ?? [];
            var isCsvBatch = !string.IsNullOrWhiteSpace(csvPathValue);

            if (verboseValue)
            {
                writeLine("Karl CLI starting...");
            }

            var services = new ServiceCollection();
            var karlBuilder = services.AddKarl();
            var configuration = loadConfiguration(jsonPathValue);
            karlBuilder.UseConfiguration(configuration);
            configureKarlTransport(karlBuilder, parseResult);
            karlBuilder.UseScribanTemplates();
            factoryOptions?.ConfigureServices?.Invoke(services);

            var errors = new StringBuilder();
            if (isCsvBatch)
            {
                if (!string.IsNullOrEmpty(toValue))
                {
                    errors.AppendLine("--to is not used in CSV batch mode; the recipient comes from --to-column in each row.");
                }

                if (!string.IsNullOrWhiteSpace(modelPathValue))
                {
                    errors.AppendLine("--csv and --model cannot be combined yet; see docs for planned per-row + shared token merging.");
                }

                if (string.IsNullOrWhiteSpace(toColumnValue))
                {
                    errors.AppendLine("--to-column is required when --csv is provided.");
                }
            }
            else if (string.IsNullOrEmpty(toValue))
            {
                errors.AppendLine("No to address was provided.");
            }

            if (string.IsNullOrEmpty(fromValue))
            {
                errors.AppendLine("No from address was provided.");
            }

            if (string.IsNullOrEmpty(subjectValue))
            {
                errors.AppendLine("No email subject was provided.");
            }

            foreach (var path in attachValues)
            {
                if (!fileExists(path))
                {
                    errors.AppendLine($"Attachment file not found: '{path}'.");
                }
            }

            if (errors.Length > 0)
            {
                write(errors.ToString());
                writeLine("You can set these values via command-line options, environment variables, or in a JSON configuration file.");
                return 1;
            }

            var provider = services.BuildServiceProvider();
            var emailService = provider.GetRequiredService<IEmailService>();
            var renderer = provider.GetRequiredService<ITemplateRenderer>();

            var markdownText = string.Empty;
            if (!string.IsNullOrWhiteSpace(markdownPathValue) && fileExists(markdownPathValue))
            {
                markdownText = readAllText(markdownPathValue);
            }

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                markdownText = bodyValue ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                writeLine("No content provided for email body.");
                return 1;
            }

            var attachments = attachValues.Select(path => EmailAttachment.FromFile(path)).ToList();

            async Task<EmailMessage> RenderMessageAsync(object? templateModel, string toAddress, string? toName)
            {
                var renderedSubject = await renderer.RenderAsync(subjectValue ?? string.Empty, templateModel, cancellationToken);
                var renderedBody = await renderer.RenderAsync(markdownText, templateModel, cancellationToken);

                var message = new EmailMessage
                {
                    To =
                    {
                        new EmailAddress(toAddress, toName)
                    },
                    From = new EmailAddress(fromValue ?? string.Empty),
                    Subject = renderedSubject.Text,
                    Body = new EmailBody
                    {
                        Text = renderedBody.Text,
                        Html = renderedBody.Html
                    }
                };
                message.Attachments.AddRange(attachments);
                return message;
            }

            if (isCsvBatch)
            {
                IReadOnlyList<IDictionary<string, string>> rows;
                try
                {
                    rows = CsvFile.ReadDynamic(csvPathValue!).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    writeLine($"Could not read CSV file '{csvPathValue}': {ex.Message}");
                    return 1;
                }

                if (rows.Count > 0 && !rows[0].ContainsKey(toColumnValue!))
                {
                    writeLine($"--to-column '{toColumnValue}' was not found in the CSV header row.");
                    return 1;
                }

                var sent = 0;
                var failed = 0;
                var skipped = 0;

                for (var i = 0; i < rows.Count; i++)
                {
                    var rowNumber = i + 1;
                    var row = rows[i];

                    if (!row.TryGetValue(toColumnValue!, out var rowToValue) || string.IsNullOrWhiteSpace(rowToValue))
                    {
                        skipped++;
                        writeLine($"[{rowNumber}/{rows.Count}] Skipping row: '{toColumnValue}' is blank.");
                        continue;
                    }

                    string? rowToName = null;
                    if (!string.IsNullOrWhiteSpace(nameColumnValue) && row.TryGetValue(nameColumnValue, out var rowNameValue) && !string.IsNullOrWhiteSpace(rowNameValue))
                    {
                        rowToName = rowNameValue;
                    }

                    if (verboseValue)
                    {
                        writeLine($"[{rowNumber}/{rows.Count}] Sending to {rowToValue}...");
                    }

                    try
                    {
                        var rowMessage = await RenderMessageAsync(row, rowToValue, rowToName);
                        await emailService.SendAsync(rowMessage, cancellationToken);
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        failed++;
                        writeLine($"[{rowNumber}/{rows.Count}] Failed to send to {rowToValue}: {ex.Message}");
                    }
                }

                writeLine($"Sent {sent} of {rows.Count} emails from {csvPathValue} ({skipped} skipped, {failed} failed).");

                return failed > 0 || skipped > 0 ? 1 : 0;
            }

            var modelJson = "{}";
            if (!string.IsNullOrWhiteSpace(modelPathValue) && fileExists(modelPathValue))
            {
                modelJson = readAllText(modelPathValue);
            }

            var model = JsonSerializer.Deserialize<object>(modelJson);
            var message = await RenderMessageAsync(model, toValue ?? string.Empty, null);

            if (verboseValue)
            {
                writeLine("Sending email...");
            }

            await emailService.SendAsync(message, cancellationToken);

            if (verboseValue)
            {
                writeLine("Done.");
            }

            return 0;
        }

        file.SetAction((parseResult, cancellationToken) =>
            HandleEmailAsync(parseResult, cancellationToken, (builder, pr) =>
            {
                var outputValue = pr.GetValue(output) ?? "emails";

                builder.UseFile(options =>
                {
                    options.DirectoryPath = outputValue;
                    options.FileNamePrefix = "email";
                });
            })
        );

        send.SetAction((parseResult, cancellationToken) =>
            HandleEmailAsync(parseResult, cancellationToken, (builder, pr) =>
            {
                var smtpHostValue = pr.GetValue(smtpHost);
                var smtpPortValue = pr.GetValue(smtpPort);
                var usernameValue = pr.GetValue(username);
                var passwordValue = pr.GetValue(password);
                var tlsValue = pr.GetValue(tls);

                builder.UseSmtp(options =>
                {
                    options.Host = smtpHostValue ?? "localhost";
                    options.Port = smtpPortValue != 0 ? smtpPortValue : 25;
                    options.Username = usernameValue ?? string.Empty;
                    options.Password = passwordValue ?? string.Empty;
                    options.SecurityMode = string.IsNullOrWhiteSpace(tlsValue) ? "StartTlsRequired" : tlsValue;
                });
            })
        );

        preview.SetAction((parseResult, cancellationToken) =>
            HandleEmailAsync(parseResult, cancellationToken, (builder, _) => builder.UseStdOut())
        );

        root.Add(send);
        root.Add(file);
        root.Add(preview);

        return root;
   }
}
