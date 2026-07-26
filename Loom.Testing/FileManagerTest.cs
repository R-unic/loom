using Loom.Core;
using Loom.Core.Pipeline;

namespace Loom.Testing;

[Collection("Assembly")]
public class FileManagerTest
{
    [Fact]
    public void Loads_Single()
    {
        var file = FileManager.LoadSingle($"{AssemblyFixture.Snapshots}/src/basic_binary.loom");
        Assert.Equal("basic_binary.loom", file.Name);
        Assert.Equal($"{AssemblyFixture.Snapshots}{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}basic_binary.loom", file.RelativePath());
        Assert.Equal($"src{Path.DirectorySeparatorChar}basic_binary.loom", file.RelativePath(AssemblyFixture.Snapshots));
        Assert.Equal("basic_binary.loom", file.RelativePath(AssemblyFixture.Snapshots + "/src"));
        Assert.Equal("1 + 2", file.SourceText);
    }
}