namespace Loom.Testing;

/// <summary>A directory that exists for the length of a test and is gone afterwards, whatever was written into it.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "loom-temp-" + Guid.NewGuid());

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    /// <summary>A path inside this directory, whether or not anything is there yet.</summary>
    public string At(params string[] segments) => System.IO.Path.Combine([Path, ..segments]);

    /// <summary>Writes a file, creating whatever directories its path names.</summary>
    public string Write(string relativePath, string contents)
    {
        var path = At(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a temp directory that outlives the test is the operating system's to clean up, and failing the test
            // over it would report a problem that is not the one under test
        }
    }
}
