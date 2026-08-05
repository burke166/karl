namespace Karl.Models;

internal static class AttachmentContentTypes
{
    public const string Default = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".odp"] = "application/vnd.oasis.opendocument.presentation",
    };

    public static string Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && ByExtension.TryGetValue(extension, out var contentType)
            ? contentType
            : Default;
    }
}
