using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Karl.Transport.Smtp;

internal sealed class MailKitSmtpClientAdapter : ISmtpClientAdapter
{
    private readonly SmtpClient _client = new();

    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken) =>
        _client.ConnectAsync(host, port, options, cancellationToken);

    public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
        _client.AuthenticateAsync(userName, password, cancellationToken);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken) =>
        _client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken) =>
        _client.DisconnectAsync(quit, cancellationToken);

    public void Dispose() =>
        _client.Dispose();
}

internal sealed class MailKitSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClientAdapter Create() => new MailKitSmtpClientAdapter();
}
