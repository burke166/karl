using Karl.Templates.Scriban;
using Karl.Transport.File;
using Karl.Transport.Smtp;
using Karl.Transport.StdOut;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace Karl.Extensions.Microsoft;

public static class ServiceCollectionExtensions
{
    public static IKarlBuilder AddKarl(this IServiceCollection services)
    {
        services.AddSingleton<IEmailService, EmailService>();
        return new KarlBuilder(services);
    }

    public static IKarlBuilder UseStdOut(this IKarlBuilder builder)
    {
        builder.Services.AddSingleton<IEmailTransport>(sp =>
        {
            return new StdOutEmailTransport();
        });

        return builder;
    }

    public static IKarlBuilder UseSmtp(this IKarlBuilder builder, Action<SmtpTransportOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.AddSingleton<IEmailTransport>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SmtpTransportOptions>>().Value;
            return new SmtpTransport(options);
        });
        return builder;
    }

    public static IKarlBuilder UseFile(this IKarlBuilder builder, Action<FileTransportOptions>? configure = null)
    {
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }
        else
        {
            builder.Services.Configure<FileTransportOptions>(_ => { });
        }

        builder.Services.AddSingleton<IEmailTransport>(sp => 
        {
            var options = sp.GetRequiredService<IOptions<FileTransportOptions>>().Value;
            return new FileEmailTransport(options);
        });
        return builder;
    }

    public static IKarlBuilder UseScribanTemplates(this IKarlBuilder builder)
    {
        builder.Services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        return builder;
    }

    public static IKarlBuilder UseConfiguration(this IKarlBuilder builder, string filePath = "karl.json")
    {
        var configBuilder = new ConfigurationBuilder();

        string userConfigPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".karl", "karl.json")
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                "karl", "karl.json");

        if (File.Exists(userConfigPath))
            configBuilder.AddJsonFile(userConfigPath);

        var localConfig = Path.Combine(Environment.CurrentDirectory, filePath);
        if (File.Exists(localConfig))
            configBuilder.AddJsonFile(localConfig);

        configBuilder.AddEnvironmentVariables("KARL_");

        var config = configBuilder.Build();

        return builder;
    }
}