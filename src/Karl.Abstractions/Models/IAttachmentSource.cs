namespace Karl.Models;

public interface IAttachmentSource
{
    bool OwnsStream { get; }

    Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}
