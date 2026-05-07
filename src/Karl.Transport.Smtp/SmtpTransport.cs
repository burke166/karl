using Karl.Models;
using MimeKit;
using MailKit.Security;

namespace Karl.Transport.Smtp;

public class SmtpTransport : IEmailTransport
{
    private readonly SmtpTransportOptions _options;
    private readonly ISmtpClientFactory _smtpClientFactory;

    public SmtpTransport(SmtpTransportOptions options)
        : this(options, new MailKitSmtpClientFactory())
    {
    }

    internal SmtpTransport(SmtpTransportOptions options, ISmtpClientFactory smtpClientFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _smtpClientFactory = smtpClientFactory ?? throw new ArgumentNullException(nameof(smtpClientFactory));
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mimeMessage = new MimeMessage();

        mimeMessage.From.Add(new MailboxAddress(message.From.Name ?? string.Empty, message.From.Address));

        foreach (var to in message.To)
        {
            mimeMessage.To.Add(new MailboxAddress(to.Name ?? string.Empty, to.Address));
        }

        foreach (var cc in message.Cc)
        {
            mimeMessage.Cc.Add(new MailboxAddress(cc.Name ?? string.Empty, cc.Address));
        }

        foreach (var bcc in message.Bcc)
        {
            mimeMessage.Bcc.Add(new MailboxAddress(bcc.Name ?? string.Empty, bcc.Address));
        }

        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder
        {
            TextBody = message.Body.Text,
            HtmlBody = message.Body.Html
        };

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        var secureSocketOptions = _options.SecurityMode.ToLowerInvariant() switch
        {
            "none" => SecureSocketOptions.None,
            "implicittls" => SecureSocketOptions.SslOnConnect,
            "starttlsrequired" => SecureSocketOptions.StartTls,
            "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
            _ => SecureSocketOptions.StartTls
        };

        using var client = _smtpClientFactory.Create();
        var isConnected = false;

        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions, cancellationToken);
            isConnected = true;

            if (!string.IsNullOrEmpty(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mimeMessage, cancellationToken);
        }
        finally
        {
            if (isConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }
}
