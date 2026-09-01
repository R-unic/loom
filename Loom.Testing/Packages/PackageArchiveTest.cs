using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Loom.Config;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     A package version as it travels, and — the half that matters — as it is read back. Every archive is treated as
///     hostile whoever it came from: a client that trusts one it did not write is one compromised or misconfigured
///     registry away from writing wherever it likes on the machine.
/// </summary>
/// <remarks>
///     The refusals mirror <c>internal/publish/read_test.go</c> in <c>loom-pm</c>, which refuses the same shapes on
///     the way in. Both sides checking is the point: neither is entitled to assume the other did.
/// </remarks>
[Collection("Assembly")]
public class PackageArchiveTest
{
    private const string Manifest =
        "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n";

    [Fact]
    public void RoundTrips_AVersionsFiles_AtThePathsItNamesThem()
    {
        using var directory = new TemporaryDirectory();
        directory.Write(Path.Combine("source", ConfigReader.ConfigFileName), Manifest);
        directory.Write(Path.Combine("source", "src", "init.loom"), "export let pi = 3;");
        directory.Write(Path.Combine("source", "src", "vector", "init.loom"), "export let zero = 0;");
        var payload = new PackagePayload(
            PackageName.Parse("math"),
            Version.Parse("1.0.0"),
            directory.At("source"),
            [ConfigReader.ConfigFileName, Path.Combine("src", "init.loom"), Path.Combine("src", "vector", "init.loom")]
        );

        var content = PackageArchive.Create(payload, out var written);
        Assert.Empty(written);
        Assert.NotNull(content);
        Assert.True(PackageArchive.Extract(content, directory.At("installed"), out var read));
        Assert.Empty(read);
        Assert.Equal("export let pi = 3;", File.ReadAllText(directory.At("installed", "src", "init.loom")));
        Assert.Equal("export let zero = 0;", File.ReadAllText(directory.At("installed", "src", "vector", "init.loom")));
        Assert.Equal(Version.Parse("1.0.0"), ConfigReader.LocateFromDirectory(directory.At("installed"), out _)!.Package!.Version);
    }

    [Fact]
    public void Create_ReportsAFileThatIsNotThere()
    {
        using var directory = new TemporaryDirectory();
        var payload = new PackagePayload(PackageName.Parse("math"), Version.Parse("1.0.0"), directory.At("source"), ["missing.loom"]);
        Assert.Null(PackageArchive.Create(payload, out var diagnostics));
        Assert.Contains("could not read", Assert.Single(diagnostics).Message);
    }

    /// <remarks>What is installed has to be one version, not the union of it and whatever was there before.</remarks>
    [Fact]
    public void Extract_ReplacesWhateverWasInTheDirectory()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.At("installed");
        directory.Write(Path.Combine("installed", "left-behind.loom"), "let x = 1;");
        Assert.True(PackageArchive.Extract(Archive(("src/init.loom", "export let pi = 3;")), destination, out _));
        Assert.False(File.Exists(Path.Combine(destination, "left-behind.loom")));
        Assert.True(File.Exists(Path.Combine(destination, "src", "init.loom")));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("//etc/passwd")]
    [InlineData("../escaped.loom")]
    [InlineData("src/../../escaped.loom")]
    [InlineData("./../escaped.loom")]
    [InlineData("..")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("./")]
    public void Extract_RefusesAnEntryThatIsNotAPathInsideThePackage(string name)
    {
        using var directory = new TemporaryDirectory();
        Assert.False(PackageArchive.Extract(Archive((name, "let x = 1;")), directory.At("installed"), out var diagnostics));
        Assert.Contains("the package archive was refused", Assert.Single(diagnostics).Message);
        Assert.False(Directory.Exists(directory.At("installed")));
    }

    /// <remarks>
    ///     A backslash is an ordinary character in a tar entry and a directory separator on Windows, so <c>a\b</c> is
    ///     one file on Linux and two nested ones here. Rather than pick a reading, it is refused: an archive spelling
    ///     a path that way is not describing the same package to everybody.
    /// </remarks>
    [Fact]
    public void Extract_RefusesAnEntryNamedWithABackslash()
    {
        using var directory = new TemporaryDirectory();
        Assert.False(PackageArchive.Extract(Archive((@"src\init.loom", "let x = 1;")), directory.At("installed"), out var diagnostics));
        Assert.Contains("is not a path inside the package", Assert.Single(diagnostics).Message);
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    [InlineData(TarEntryType.CharacterDevice)]
    [InlineData(TarEntryType.BlockDevice)]
    [InlineData(TarEntryType.Fifo)]
    public void Extract_RefusesAnEntryThatIsNotAFile(TarEntryType type)
    {
        using var directory = new TemporaryDirectory();
        var content = Bytes(
            archive =>
            {
                var entry = new PaxTarEntry(type, "src/init.loom");
                if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                    entry.LinkName = "/etc/passwd";

                archive.WriteEntry(entry);
            }
        );

        Assert.False(PackageArchive.Extract(content, directory.At("installed"), out var diagnostics));
        Assert.Contains("rather than a file", Assert.Single(diagnostics).Message);
        Assert.False(Directory.Exists(directory.At("installed")));
    }

    /// <remarks>
    ///     Which of the two the package holds would be whichever was unpacked last, which is not something an index
    ///     may decide silently — so what was published is left unanswerable and the archive is refused.
    /// </remarks>
    [Fact]
    public void Extract_RefusesTheSameFileTwice()
    {
        using var directory = new TemporaryDirectory();
        var content = Archive(("src/init.loom", "export let pi = 3;"), ("src/init.loom", "export let pi = 4;"));
        Assert.False(PackageArchive.Extract(content, directory.At("installed"), out var diagnostics));
        Assert.Contains("appears more than once", Assert.Single(diagnostics).Message);
    }

    /// <remarks>Counted as it is copied rather than believed off the header: an archive written to get past a check states whatever does.</remarks>
    [Fact]
    public void Extract_RefusesAnArchiveThatUnpacksToMoreThanAPackageMay()
    {
        using var directory = new TemporaryDirectory();
        var content = Bytes(archive => archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "big.loom") { DataStream = new MemoryStream(new byte[65 * 1024 * 1024]) }));

        Assert.False(PackageArchive.Extract(content, directory.At("installed"), out var diagnostics));
        Assert.Contains("unpacks to more than", Assert.Single(diagnostics).Message);
        Assert.False(Directory.Exists(directory.At("installed")));
    }

    /// <remarks>The same guard for an archive that is small because its millions of entries are all empty.</remarks>
    [Fact]
    public void Extract_RefusesAnArchiveOfMoreEntriesThanAPackageMayHold()
    {
        using var directory = new TemporaryDirectory();
        var content = Bytes(
            archive =>
            {
                for (var entry = 0; entry <= 20_000; entry++)
                    archive.WriteEntry(new UstarTarEntry(TarEntryType.Directory, "src/"));
            },
            TarEntryFormat.Ustar
        );

        Assert.False(PackageArchive.Extract(content, directory.At("installed"), out var diagnostics));
        Assert.Contains("more than 20000 entries", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Extract_RefusesSomethingThatIsNotAnArchive()
    {
        using var directory = new TemporaryDirectory();

        Assert.False(PackageArchive.Extract("not a gzip stream at all"u8.ToArray(), directory.At("installed"), out var diagnostics));
        Assert.NotEmpty(diagnostics);
        Assert.False(Directory.Exists(directory.At("installed")));
    }

    /// <remarks>
    ///     A refused archive leaves nothing behind, including beside the destination: extraction stages into a sibling
    ///     directory and moves, so an archive rejected halfway never leaves part of a package where the compiler reads
    ///     a whole one.
    /// </remarks>
    [Fact]
    public void Extract_LeavesNothingBeside_TheDirectoryItRefusedToFill()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.At("packages", "math");

        Assert.False(PackageArchive.Extract(Archive(("ok.loom", "let x = 1;"), ("../escaped.loom", "let x = 2;")), destination, out _));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetFileSystemEntries(directory.At("packages")));
        Assert.False(File.Exists(directory.At("escaped.loom")));
    }

    /// <remarks>A directory entry is how an archive carries an empty one, and refusing it would refuse ordinary archives.</remarks>
    [Fact]
    public void Extract_CreatesTheDirectoriesAnArchiveNames()
    {
        using var directory = new TemporaryDirectory();
        var content = Bytes(
            archive =>
            {
                archive.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "src/empty/"));
                archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./src/init.loom") { DataStream = new MemoryStream("let x = 1;"u8.ToArray()) });
            }
        );

        Assert.True(PackageArchive.Extract(content, directory.At("installed"), out var diagnostics));
        Assert.Empty(diagnostics);
        Assert.True(Directory.Exists(directory.At("installed", "src", "empty")));
        Assert.Equal("let x = 1;", File.ReadAllText(directory.At("installed", "src", "init.loom")));
    }

    /// <summary>A gzipped tar holding exactly the entries named, written directly so a case can name one no publish would.</summary>
    private static byte[] Archive(params (string Name, string Contents)[] entries) =>
        Bytes(
            archive =>
            {
                foreach (var (name, contents) in entries)
                    archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents)) });
            }
        );

    private static byte[] Bytes(Action<TarWriter> write, TarEntryFormat format = TarEntryFormat.Pax)
    {
        using var buffer = new MemoryStream();
        using (var compressed = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            using (var archive = new TarWriter(compressed, format, leaveOpen: true))
                write(archive);

        return buffer.ToArray();
    }
}
