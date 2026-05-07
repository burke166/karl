namespace Karl.Test;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore best-effort cleanup failures on transient file locks.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore best-effort cleanup failures due to permissions.
        }
    }
}
