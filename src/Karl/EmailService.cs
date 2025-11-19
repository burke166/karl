using Karl.Models;

namespace Karl;

public class EmailService : IEmailService
{
    private readonly IEmailTransport _transport;

    public EmailService(IEmailTransport transport)
    {
        _transport = transport;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        return _transport.SendAsync(message, cancellationToken);
    }
}
