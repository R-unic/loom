using Loom.Config;
using Loom.Core.Modules;
using Loom.Core.Pipeline;
using Loom.LanguageServer;
using Version = Loom.Config.Version;

namespace Loom.Testing.LanguageServer;

/// <summary>Where a name in scope came from - the file itself, an import, a package, or an ambient declaration.</summary>
[Collection("Assembly")]
public class SymbolOriginTest
{
    [Fact]
    public void Describe_ForASymbolFromADependencyPackage_NamesThePackage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-origin-test-" + Guid.NewGuid());
        var packageDirectory = Path.Combine(directory, "packages", "math");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(packageDirectory, "src"));
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "loom-config.toml"),
                "[dependencies]\nmath = \"^1.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );
            File.WriteAllText(
                Path.Combine(packageDirectory, "loom-config.toml"),
                "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );
            File.WriteAllText(Path.Combine(packageDirectory, "src", "init.loom"), "export let pi = 3;");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "import { pi } from \"math\";\nlet x: number = pi;");
            new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]).WriteTo(directory);

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var roots = ProjectLoader.Load(config, out _);
            Assert.NotNull(roots);

            var unit = new CompilationUnit(roots);
            var result = unit.Compile();
            Utility.AssertNoErrors(result);

            var mainFile = result.Files.Single(file => file.SourceFile.Name == "main.loom");
            var piSymbol = mainFile.SemanticModel.References.Values.SelectMany(symbols => symbols).Single(symbol => symbol.Name == "pi");

            var resolver = new ModuleResolver(unit.SourceFiles, unit.Roots);
            var origin = SymbolOrigin.Describe(piSymbol, mainFile.SourceFile, unit, resolver);

            Assert.Equal("package `math`", origin);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Describe_ForAnAmbientGlobalFromADeclarationFile_SaysWhichFileDeclaredIt() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var symbol = state.File.SemanticModel.References.Values.SelectMany(symbols => symbols).Single(candidate => candidate.Name == "version");

                var resolver = new ModuleResolver(state.Unit.SourceFiles, state.Unit.Roots);
                var origin = SymbolOrigin.Describe(symbol, state.File.SourceFile, state.Unit, resolver);

                Assert.Equal("ambient, declared in `globals.d.loom`", origin);
                return Task.CompletedTask;
            },
            "print(version);",
            ("globals.d.loom", "declare let version: string;")
        );
}
