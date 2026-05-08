using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
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

        var root = new RootCommand("karl", "Karl CLI - The Mailman Delivers");
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

        var tls = new Option<string>("--tls", ["-t"])
        {
            Description = "STARTTLS behavior",
            Required = false
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

        async Task<int> HandleEmailAsync(ParseResult parseResult, Action<IKarlBuilder, ParseResult> configureKarlTransport)
        {
            var verboseValue = parseResult.GetValue(verbose);
            var fromValue = parseResult.GetValue(from);
            var toValue = parseResult.GetValue(to);
            var subjectValue = parseResult.GetValue(subject);
            var jsonPathValue = parseResult.GetValue(jsonPath);
            var markdownPathValue = parseResult.GetValue(markdownPath);
            var bodyValue = parseResult.GetValue(body);
            var modelPathValue = parseResult.GetValue(modelPath);

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
            if (string.IsNullOrEmpty(toValue))
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

            if (errors.Length > 0)
            {
                write(errors.ToString());
                writeLine("You can set these values via command-line options, environment variables, or in a JSON configuration file.");
                return 1;
            }

            var provider = services.BuildServiceProvider();
            var emailService = provider.GetRequiredService<IEmailService>();
            var renderer = provider.GetRequiredService<ITemplateRenderer>();

            var modelJson = "{}";
            if (!string.IsNullOrWhiteSpace(modelPathValue) && fileExists(modelPathValue))
            {
                modelJson = readAllText(modelPathValue);
            }

            var model = JsonSerializer.Deserialize<object>(modelJson);

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

            var renderedSubject = await renderer.RenderAsync(subjectValue ?? string.Empty, model);
            var renderedBody = await renderer.RenderAsync(markdownText, model);

            var message = new EmailMessage
            {
                To =
                {
                    new EmailAddress(toValue ?? string.Empty)
                },
                From = new EmailAddress(fromValue ?? string.Empty),
                Subject = renderedSubject.Text,
                Body = new EmailBody
                {
                    Text = renderedBody.Text,
                    Html = renderedBody.Html
                }
            };

            if (verboseValue)
            {
                writeLine("Sending email...");
            }

            await emailService.SendAsync(message);

            if (verboseValue)
            {
                writeLine("Done.");
            }

            return 0;
        }

        file.SetAction(parseResult =>
            HandleEmailAsync(parseResult, (builder, pr) =>
            {
                var outputValue = pr.GetValue(output) ?? "emails";

                builder.UseFile(options =>
                {
                    options.DirectoryPath = outputValue;
                    options.FileNamePrefix = "email";
                });
            })
        );

        send.SetAction(parseResult =>
            HandleEmailAsync(parseResult, (builder, pr) =>
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

        preview.SetAction(parseResult =>
            HandleEmailAsync(parseResult, (builder, _) => builder.UseStdOut())
        );

        root.Add(send);
        root.Add(file);
        root.Add(preview);
        return root;
    }
}
