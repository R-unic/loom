using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;
using Loom.Testing;

namespace Loom.Testing.Modules;

public partial class SourceRootTest
{
    [Fact]
    public void Rejects_ImportOfAnInternalMember_FromADependency()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.InternalMemberOutsideRoot,
                "'hash_key' is internal to module 'init.loom', so a different root cannot import it."
            ),
            appFiles: [("main.loom", "import { hash_key } from \"math\"\nprint(hash_key(1));")],
            packageFiles: [("init.loom", "export let pi = 3;\ninternal fn hash_key(k: number): number -> k;")]
        );

    [Fact]
    public void Imports_APublicMember_FromADependency_EvenWhenItAlsoDeclaresInternalOnes()
        => WithWorkspace((_, unit) => Utility.AssertNoErrors(unit.Compile()),
            appFiles: [("main.loom", "import { pi } from \"math\"\nprint(pi);")],
            packageFiles: [("init.loom", "export let pi = 3;\ninternal fn hash_key(k: number): number -> k;")],
            rojoProject: AppRojoProject
        );

    [Fact]
    public void Rejects_ReExportOfAnInternalMember_FromADependency()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.InternalMemberOutsideRoot,
                "'hash_key' is internal to module 'init.loom', so it cannot be re-exported from a different root."
            ),
            appFiles: [("main.loom", "export { hash_key } from \"math\"")],
            packageFiles: [("init.loom", "internal fn hash_key(k: number): number -> k;")]
        );

    [Fact]
    public void ExcludesInternalMembers_FromAStarReExport_OfADependency()
        => WithWorkspace((_, unit) =>
            {
                Utility.AssertNoErrors(unit.Compile());

                var main = unit.AnalyzedModules.Values.Single(model => model.Tree.File.Name == "main.loom");
                Assert.Equal(["pi"], main.Exports.Select(export => export.Name));
            },
            appFiles: [("main.loom", "export * from \"math\"")],
            packageFiles: [("init.loom", "export let pi = 3;\ninternal fn hash_key(k: number): number -> k;")],
            rojoProject: AppRojoProject
        );

    [Fact]
    public void ExcludesInternalMembers_FromANamespaceImport_OfADependency()
        => WithWorkspace((_, unit) =>
            {
                Utility.AssertNoErrors(unit.Compile());

                var main = unit.AnalyzedModules.Values.Single(model => model.Tree.File.Name == "main.loom");
                var binding = Assert.Single(main.NamespaceImports);
                var namespaceType = Assert.IsType<ObjectType>(main.GetType(binding.Import));
                Assert.Equal(["pi"], namespaceType.Properties.Select(property => property.Name));
            },
            appFiles: [("main.loom", "import * as math from \"math\"\nprint(math::pi);")],
            packageFiles: [("init.loom", "export let pi = 3;\ninternal fn hash_key(k: number): number -> k;")],
            rojoProject: AppRojoProject
        );
}
