using Karl.Core;
using Karl.Models;

namespace Karl.Test;

public class EmailServiceTests
{
    [Fact]
    public async Task SendAsync_ForwardsMessageAndCancellationTokenToTransport()
    {
        var transport = new RecordingTransport();
        var sut = new EmailService(transport);
        var message = new MailBuilder().From("sender@example.com").To("to@example.com").Build();
        using var cts = new CancellationTokenSource();

        await sut.SendAsync(message, cts.Token);

        Assert.Same(message, transport.Message);
        Assert.Equal(cts.Token, transport.Token);
    }

    private sealed class RecordingTransport : IEmailTransport
    {
        public EmailMessage? Message { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Message = message;
            Token = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
