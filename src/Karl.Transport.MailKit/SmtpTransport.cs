using Karl.Models;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Karl.Transport.Smtp;

public class SmtpTransport : IEmailTransport
{
    private readonly SmtpTransportOptions _options;

    public SmtpTransport(SmtpTransportOptions options)
    {
        _options = options;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
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

        using var client = new SmtpClient();

        var secureSocketOptions = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
