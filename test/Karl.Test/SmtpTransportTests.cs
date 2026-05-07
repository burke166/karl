using Karl.Models;
using Karl.Transport.Smtp;
using MailKit.Security;
using MimeKit;

namespace Karl.Test;

public class SmtpTransportTests
{
    [Fact]
    public async Task SendAsync_WithValidMessage_ConnectsSendsAndDisconnects()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var options = CreateOptions();
        options.Username = null;
        options.Password = null;
        var sut = new SmtpTransport(options, new FakeSmtpClientFactory(fakeClient));
        var message = CreateMessage();

        await sut.SendAsync(message);

        Assert.Equal(new[] { "Connect", "Send", "Disconnect" }, fakeClient.Calls);
    }

    [Fact]
    public async Task SendAsync_WithoutCredentials_DoesNotAuthenticate()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var options = CreateOptions();
        options.Username = null;
        options.Password = null;
        var sut = new SmtpTransport(options, new FakeSmtpClientFactory(fakeClient));

        await sut.SendAsync(CreateMessage());

        Assert.DoesNotContain("Authenticate", fakeClient.Calls);
    }

    [Fact]
    public async Task SendAsync_WithCredentials_AuthenticatesBeforeSending()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var sut = CreateSut(fakeClient);

        await sut.SendAsync(CreateMessage());

        Assert.Equal(new[] { "Connect", "Authenticate", "Send", "Disconnect" }, fakeClient.Calls);
    }

    [Theory]
    [InlineData("none", SecureSocketOptions.None)]
    [InlineData("implicittls", SecureSocketOptions.SslOnConnect)]
    [InlineData("starttlsrequired", SecureSocketOptions.StartTls)]
    [InlineData("starttlswhenavailable", SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData("anything-else", SecureSocketOptions.StartTls)]
    public async Task SendAsync_UsesExpectedSecureSocketOptions(string securityMode, SecureSocketOptions expected)
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var options = CreateOptions();
        options.SecurityMode = securityMode;
        var sut = new SmtpTransport(options, new FakeSmtpClientFactory(fakeClient));

        await sut.SendAsync(CreateMessage());

        Assert.Equal(expected, fakeClient.SecureSocketOptions);
    }

    [Fact]
    public async Task SendAsync_MapsBasicMessageFields()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var sut = CreateSut(fakeClient);
        var message = new EmailMessage
        {
            From = new EmailAddress("from@example.com", "From Name"),
            Subject = "Subject Line",
            Body = new EmailBody { Text = "plain text body" }
        };
        message.To.Add(new EmailAddress("to@example.com", "To Name"));
        message.Cc.Add(new EmailAddress("cc@example.com", "Cc Name"));
        message.Bcc.Add(new EmailAddress("bcc@example.com", "Bcc Name"));

        await sut.SendAsync(message);

        var sent = Assert.IsType<MimeMessage>(fakeClient.SentMessage);
        Assert.Equal("Subject Line", sent.Subject);
        Assert.Equal("From Name", ((MailboxAddress)sent.From[0]).Name);
        Assert.Equal("from@example.com", ((MailboxAddress)sent.From[0]).Address);
        Assert.Equal("to@example.com", ((MailboxAddress)sent.To[0]).Address);
        Assert.Equal("cc@example.com", ((MailboxAddress)sent.Cc[0]).Address);
        Assert.Equal("bcc@example.com", ((MailboxAddress)sent.Bcc[0]).Address);
        var textBody = Assert.IsType<TextPart>(sent.Body);
        Assert.Equal("plain text body", textBody.Text);
    }

    [Fact]
    public async Task SendAsync_WithHtmlAndText_CreatesMultipartAlternative()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var sut = CreateSut(fakeClient);
        var message = CreateMessage();
        message.Body = new EmailBody
        {
            Text = "text body",
            Html = "<p>html body</p>"
        };

        await sut.SendAsync(message);

        var multipart = Assert.IsType<MultipartAlternative>(fakeClient.SentMessage!.Body);
        Assert.Contains(multipart.OfType<TextPart>(), part => part.IsPlain && part.Text == "text body");
        Assert.Contains(multipart.OfType<TextPart>(), part => part.IsHtml && part.Text == "<p>html body</p>");
    }

    [Fact]
    public async Task SendAsync_PassesCancellationTokenToAllAsyncCalls()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var sut = CreateSut(fakeClient);
        using var cts = new CancellationTokenSource();

        await sut.SendAsync(CreateMessage(), cts.Token);

        Assert.Equal(cts.Token, fakeClient.ConnectToken);
        Assert.Equal(cts.Token, fakeClient.AuthenticateToken);
        Assert.Equal(cts.Token, fakeClient.SendToken);
        Assert.Equal(cts.Token, fakeClient.DisconnectToken);
    }

    [Fact]
    public async Task SendAsync_WhenConnectFails_DoesNotSendOrAuthenticate()
    {
        var fakeClient = new FakeSmtpClientAdapter
        {
            ConnectException = new InvalidOperationException("connect failed")
        };
        var sut = CreateSut(fakeClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendAsync(CreateMessage()));

        Assert.Equal(new[] { "Connect" }, fakeClient.Calls);
        Assert.DoesNotContain("Authenticate", fakeClient.Calls);
        Assert.DoesNotContain("Send", fakeClient.Calls);
        Assert.DoesNotContain("Disconnect", fakeClient.Calls);
    }

    [Fact]
    public async Task SendAsync_WhenAuthenticationFails_DoesNotSend()
    {
        var fakeClient = new FakeSmtpClientAdapter
        {
            AuthenticateException = new InvalidOperationException("auth failed")
        };
        var sut = CreateSut(fakeClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendAsync(CreateMessage()));

        Assert.Equal(new[] { "Connect", "Authenticate", "Disconnect" }, fakeClient.Calls);
        Assert.DoesNotContain("Send", fakeClient.Calls);
    }

    [Fact]
    public async Task SendAsync_WhenSendFails_AttemptsDisconnect()
    {
        var fakeClient = new FakeSmtpClientAdapter
        {
            SendException = new InvalidOperationException("send failed")
        };
        var sut = CreateSut(fakeClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendAsync(CreateMessage()));

        Assert.Equal(new[] { "Connect", "Authenticate", "Send", "Disconnect" }, fakeClient.Calls);
    }

    [Fact]
    public async Task SendAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var fakeClient = new FakeSmtpClientAdapter();
        var sut = CreateSut(fakeClient);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SendAsync(null!));
    }

    private static SmtpTransport CreateSut(FakeSmtpClientAdapter fakeClient)
    {
        return new SmtpTransport(CreateOptions(), new FakeSmtpClientFactory(fakeClient));
    }

    private static SmtpTransportOptions CreateOptions()
    {
        return new SmtpTransportOptions
        {
            Host = "smtp.example.com",
            Port = 2525,
            SecurityMode = "StartTlsRequired",
            Username = "user",
            Password = "pass"
        };
    }

    private static EmailMessage CreateMessage()
    {
        var message = new EmailMessage
        {
            From = new EmailAddress("from@example.com", "Sender"),
            Subject = "test subject",
            Body = new EmailBody { Text = "test body" }
        };
        message.To.Add(new EmailAddress("to@example.com", "Recipient"));
        return message;
    }

    private sealed class FakeSmtpClientFactory(FakeSmtpClientAdapter adapter) : ISmtpClientFactory
    {
        public ISmtpClientAdapter Create() => adapter;
    }

    private sealed class FakeSmtpClientAdapter : ISmtpClientAdapter
    {
        public List<string> Calls { get; } = new();
        public string? Host { get; private set; }
        public int Port { get; private set; }
        public SecureSocketOptions? SecureSocketOptions { get; private set; }
        public string? Username { get; private set; }
        public string? Password { get; private set; }
        public MimeMessage? SentMessage { get; private set; }
        public CancellationToken ConnectToken { get; private set; }
        public CancellationToken AuthenticateToken { get; private set; }
        public CancellationToken SendToken { get; private set; }
        public CancellationToken DisconnectToken { get; private set; }
        public Exception? ConnectException { get; set; }
        public Exception? AuthenticateException { get; set; }
        public Exception? SendException { get; set; }

        public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken)
        {
            Calls.Add("Connect");
            Host = host;
            Port = port;
            SecureSocketOptions = options;
            ConnectToken = cancellationToken;
            return ConnectException is null ? Task.CompletedTask : Task.FromException(ConnectException);
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            Calls.Add("Authenticate");
            Username = userName;
            Password = password;
            AuthenticateToken = cancellationToken;
            return AuthenticateException is null ? Task.CompletedTask : Task.FromException(AuthenticateException);
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            Calls.Add("Send");
            SentMessage = message;
            SendToken = cancellationToken;
            return SendException is null ? Task.CompletedTask : Task.FromException(SendException);
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            Calls.Add("Disconnect");
            DisconnectToken = cancellationToken;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
