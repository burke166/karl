namespace Karl.Transport.File;

public class FileTransportOptions
{
    public string DirectoryPath { get; set; } = "outgoing-mail";
    public string FileNamePrefix { get; set; } = "mail";
}
