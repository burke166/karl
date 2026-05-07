using Karl.Extensions.Microsoft;
using Karl.Transport.File;
using Karl.Transport.Smtp;
using Karl.Transport.StdOut;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Karl.Test;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddKarlAndUseStdOut_RegistersEmailServiceAndStdOutTransport()
    {
        var services = new ServiceCollection();

        services
            .AddKarl()
            .UseStdOut();

        using var provider = services.BuildServiceProvider();
        var emailService = provider.GetRequiredService<IEmailService>();
        var emailTransport = provider.GetRequiredService<IEmailTransport>();

        Assert.NotNull(emailService);
        Assert.IsType<StdOutEmailTransport>(emailTransport);
    }

    [Fact]
    public void AddKarlAndUseFile_WithConfigure_BindsFileOptionsAndRegistersFileTransport()
    {
        var services = new ServiceCollection();

        services
            .AddKarl()
            .UseFile(options =>
            {
                options.DirectoryPath = "mail-out";
                options.FileNamePrefix = "custom";
            });

        using var provider = services.BuildServiceProvider();
        var emailTransport = provider.GetRequiredService<IEmailTransport>();
        var options = provider.GetRequiredService<IOptions<FileTransportOptions>>().Value;

        Assert.IsType<FileEmailTransport>(emailTransport);
        Assert.Equal("mail-out", options.DirectoryPath);
        Assert.Equal("custom", options.FileNamePrefix);
    }

    [Fact]
    public void AddKarlAndUseFile_WithoutConfigure_UsesDefaultFileOptions()
    {
        var services = new ServiceCollection();

        services
            .AddKarl()
            .UseFile();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FileTransportOptions>>().Value;

        Assert.Equal("outgoing-mail", options.DirectoryPath);
        Assert.Equal("mail", options.FileNamePrefix);
    }

    [Fact]
    public void AddKarlAndUseSmtp_BindsSmtpOptionsAndRegistersSmtpTransport()
    {
        var services = new ServiceCollection();

        services
            .AddKarl()
            .UseSmtp(options =>
            {
                options.Host = "smtp.example.com";
                options.Port = 2525;
                options.SecurityMode = "StartTlsWhenAvailable";
                options.Username = "user";
                options.Password = "pass";
            });

        using var provider = services.BuildServiceProvider();
        var emailTransport = provider.GetRequiredService<IEmailTransport>();
        var options = provider.GetRequiredService<IOptions<SmtpTransportOptions>>().Value;

        Assert.IsType<SmtpTransport>(emailTransport);
        Assert.Equal("smtp.example.com", options.Host);
        Assert.Equal(2525, options.Port);
        Assert.Equal("StartTlsWhenAvailable", options.SecurityMode);
        Assert.Equal("user", options.Username);
        Assert.Equal("pass", options.Password);
    }

    [Fact]
    public void UseConfiguration_BindsSmtpOptions()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?>
        {
            ["Karl:Smtp:Host"] = "smtp.example.com",
            ["Karl:Smtp:Port"] = "587",
            ["Karl:Smtp:SecurityMode"] = "StartTlsRequired"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        services
            .AddKarl()
            .UseConfiguration(configuration)
            .UseSmtp(_ => { });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SmtpTransportOptions>>().Value;

        Assert.Equal("smtp.example.com", options.Host);
        Assert.Equal(587, options.Port);
        Assert.Equal("StartTlsRequired", options.SecurityMode);
    }

    [Fact]
    public void UseConfiguration_BindsFileOptions()
    {
        var services = new ServiceCollection();
        var configData = new Dictionary<string, string?>
        {
            ["Karl:File:DirectoryPath"] = "./maildrop",
            ["Karl:File:FileNamePrefix"] = "outbound"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        services
            .AddKarl()
            .UseConfiguration(configuration)
            .UseFile();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FileTransportOptions>>().Value;

        Assert.Equal("./maildrop", options.DirectoryPath);
        Assert.Equal("outbound", options.FileNamePrefix);
    }

    [Fact]
    public void UseConfiguration_ReturnsSameBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddKarl();
        var configuration = new ConfigurationBuilder().Build();

        var returnedBuilder = builder.UseConfiguration(configuration);

        Assert.Same(builder, returnedBuilder);
    }
}
