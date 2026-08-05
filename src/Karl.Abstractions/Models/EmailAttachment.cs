namespace Karl.Models;

public sealed class EmailAttachment
{
    public string FileName { get; }
    public string ContentType { get; }
    public IAttachmentSource Source { get; }

    private EmailAttachment(string fileName, string contentType, IAttachmentSource source)
    {
        FileName = fileName;
        ContentType = contentType;
        Source = source;
    }

    public static EmailAttachment FromFile(string filePath, string? fileName = null, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var resolvedFileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(filePath) : fileName;
        var resolvedContentType = contentType ?? AttachmentContentTypes.Resolve(resolvedFileName);

        return new EmailAttachment(resolvedFileName, resolvedContentType, new FileAttachmentSource(filePath));
    }

    public static EmailAttachment FromStream(Stream stream, string fileName, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var resolvedContentType = contentType ?? AttachmentContentTypes.Resolve(fileName);

        return new EmailAttachment(fileName, resolvedContentType, new StreamAttachmentSource(stream));
    }
}
