using System.Formats.Tar;
using System.IO.Compression;
using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     A package version as it travels: a gzipped tar holding <see cref="PackagePayload.Files" /> at the paths the
///     payload names them, with no wrapping directory, so <c>loom-config.toml</c> is at the root and installing is
///     an extraction.
/// </summary>
/// <remarks>
///     <see cref="Extract" /> treats every archive as hostile, whoever it came from. A registry validates what it
///     accepts, but a client that trusts an archive it did not write is one compromised or misconfigured registry
///     away from writing wherever it likes on the machine — so the rules here are the reader's, not the writer's,
///     and are checked before a single byte reaches its destination.
/// </remarks>
public static class PackageArchive
{
    /// <summary>
    ///     A ceiling on what one package may unpack to, well above what any registry accepts. Not policy — a
    ///     registry is free to raise its own limits, and this is not the place to second-guess them — but the
    ///     guard that stops an archive whose compressed size says nothing about its unpacked one.
    /// </summary>
    private const long MaximumUnpackedBytes = 64L * 1024 * 1024;

    /// <summary>The same guard for an archive that is small because its millions of entries are all empty.</summary>
    private const int MaximumEntries = 20_000;

    /// <summary>
    ///     <paramref name="payload" /> as the bytes a registry is handed, or <see langword="null" /> having said
    ///     which file could not be read.
    /// </summary>
    public static byte[]? Create(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        try
        {
            using var buffer = new MemoryStream();
            using (var compressed = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                using (var archive = new TarWriter(compressed, TarEntryFormat.Pax, leaveOpen: true))
                {
                    foreach (var file in payload.Files)
                        archive.WriteEntry(Path.Combine(payload.Root, file), EntryName(file));
                }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics = [new ConfigDiagnostic($"could not read '{payload}' to publish it: {exception.Message}")];
            return null;
        }
    }

    /// <summary>
    ///     Unpacks <paramref name="content" /> into <paramref name="directory" />, replacing whatever is there,
    ///     and answers whether it did.
    /// </summary>
    /// <remarks>
    ///     Unpacked beside the destination and moved into place, never into it: an archive rejected halfway would
    ///     otherwise leave part of a package where the compiler reads a whole one, and half a package reads as an
    ///     installed one.
    /// </remarks>
    public static bool Extract(byte[] content, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var staging = $"{directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}.incoming-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(staging);
            if (!Unpack(content, staging, out diagnostics))
            {
                Discard(staging);
                return false;
            }

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            if (Path.GetDirectoryName(directory) is { Length: > 0 } parent)
                Directory.CreateDirectory(parent);

            Directory.Move(staging, directory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(staging);
            diagnostics = [new ConfigDiagnostic($"could not unpack into '{directory}': {exception.Message}")];
            return false;
        }
    }

    private static bool Unpack(byte[] content, string destination, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var root = Path.GetFullPath(destination);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = MaximumUnpackedBytes;
        var entries = 0;

        try
        {
            using var buffer = new MemoryStream(content, false);
            using var decompressed = new GZipStream(buffer, CompressionMode.Decompress);
            using var archive = new TarReader(decompressed);

            while (archive.GetNextEntry() is { } entry)
            {
                if (++entries > MaximumEntries)
                    return Refuse($"it holds more than {MaximumEntries} entries", out diagnostics);

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                    return Refuse($"'{entry.Name}' is a {entry.EntryType} rather than a file", out diagnostics);

                if (!Normalize(entry.Name, out var relative))
                    return Refuse($"'{entry.Name}' is not a path inside the package", out diagnostics);

                var path = Path.GetFullPath(Path.Combine(root, relative));

                // the destination is what was just created, so its full path is already normalized; an entry
                // landing outside it survived the checks above and is the one thing that must never be written
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return Refuse($"'{entry.Name}' resolves outside the package", out diagnostics);

                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                // two entries for one file leave what was published unanswerable: which of them the package
                // holds is whichever was unpacked last, which is not something an index may decide silently
                if (!written.Add(relative))
                    return Refuse($"'{entry.Name}' appears more than once", out diagnostics);

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = File.Create(path);
                if (entry.DataStream != null && !Copy(entry.DataStream, file, ref remaining))
                    return Refuse($"it unpacks to more than {MaximumUnpackedBytes / (1024 * 1024)} MB", out diagnostics);
            }

            return true;
        }
        catch (InvalidDataException exception)
        {
            return Refuse(exception.Message.TrimEnd('.'), out diagnostics);
        }
    }

    /// <summary>
    ///     An entry's path as it may be written, or <see langword="false" /> for one that may not be written at
    ///     all: rooted, escaping, or spelled with a separator that is only a separator on one platform.
    /// </summary>
    /// <remarks>
    ///     A backslash is an ordinary character in a tar entry and a directory separator on Windows, so an entry
    ///     named <c>a\b</c> is one file on Linux and two nested ones here. Rather than pick a reading, it is
    ///     refused: nothing this publishes writes one, and an archive that does is not describing the same package
    ///     to everybody.
    /// </remarks>
    private static bool Normalize(string name, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.StartsWith('/') || Path.IsPathRooted(name))
            return false;

        var segments = new List<string>();
        foreach (var segment in name.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    continue;
                case "..":
                    return false;
                default:
                    if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        return false;

                    segments.Add(segment);
                    break;
            }
        }

        if (segments.Count == 0)
            return false;

        relative = string.Join(Path.DirectorySeparatorChar, segments);
        return true;
    }

    /// <summary>
    ///     Copies what the entry actually holds, counting it rather than believing its header — a tar states a
    ///     size and an archive written to be believed states whatever gets it past a check.
    /// </summary>
    private static bool Copy(Stream source, Stream destination, ref long remaining)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return true;

            remaining -= read;
            if (remaining < 0)
                return false;

            destination.Write(buffer, 0, read);
        }
    }

    /// <summary>The path an entry is written under, which is the payload's own spelling with one separator.</summary>
    private static string EntryName(string file) => file.Replace('\\', '/');

    private static bool Refuse(string reason, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [new ConfigDiagnostic($"the package archive was refused: {reason}.")];
        return false;
    }

    private static void Discard(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // nothing further can be done about it here, and the failure being reported is the one worth reporting
        }
    }
}
