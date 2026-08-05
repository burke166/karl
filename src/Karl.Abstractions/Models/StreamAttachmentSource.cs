namespace Karl.Models;

internal sealed class StreamAttachmentSource : IAttachmentSource
{
    private readonly Stream _stream;
    private int _consumed;

    public StreamAttachmentSource(Stream stream) => _stream = stream;

    public bool OwnsStream => false;

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _consumed, 1) != 0)
        {
            throw new InvalidOperationException(
                "This attachment's stream has already been read once and cannot be read again " +
                "(for example, by sending the same EmailMessage to more than one recipient, or " +
                "reusing an EmailAttachment across CSV batch rows). Use EmailAttachment.FromFile " +
                "for an attachment that needs to be sent more than once.");
        }

        return Task.FromResult(_stream);
    }
}
