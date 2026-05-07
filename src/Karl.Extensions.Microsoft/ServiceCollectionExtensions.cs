using Karl.Core;
using Karl.Template.Scriban;
using Karl.Transport.File;
using Karl.Transport.Smtp;
using Karl.Transport.StdOut;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    public static IKarlBuilder UseConfiguration(this IKarlBuilder builder, IConfiguration configuration)
    {
        builder.Services.Configure<SmtpTransportOptions>(
            configuration.GetSection("Karl:Smtp"));

        builder.Services.Configure<FileTransportOptions>(
            configuration.GetSection("Karl:File"));

        return builder;
    }
}