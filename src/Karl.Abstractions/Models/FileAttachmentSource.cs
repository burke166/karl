namespace Karl.Models;

internal sealed class FileAttachmentSource : IAttachmentSource
{
    private readonly string _filePath;

    public FileAttachmentSource(string filePath) => _filePath = filePath;

    public bool OwnsStream => true;

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(File.OpenRead(_filePath));
    }
}
