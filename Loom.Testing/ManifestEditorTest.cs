using Loom.Config;

namespace Loom.Testing;

/// <summary>
///     Adding a dependency is a text edit on the manifest, so what these check is what the rest of the file looks
///     like afterwards: the comments, the key order and the line endings its author wrote are still theirs.
/// </summary>
[Collection("Assembly")]
public class ManifestEditorTest
{
    [Fact]
    public void WithDependency_AddsToTheEndOfAnExistingTable()
    {
        const string manifest = """
                                project_type = "library"

                                # what we depend on
                                [dependencies]
                                math = "^1.0"  # geometry needs this too

                                [realms]
                                net = "server"

                                """;

        var edited = Edit(manifest, "serio", "^2.0");

        Assert.Equal(
            """
            project_type = "library"

            # what we depend on
            [dependencies]
            math = "^1.0"  # geometry needs this too
            serio = "^2.0"

            [realms]
            net = "server"

            """,
            edited
        );
    }

    [Fact]
    public void WithDependency_AddsTheTable_WhenTheManifestHasNone()
    {
        var edited = Edit("project_type = \"library\"\n", "math", "^1.2.0");

        Assert.Equal("project_type = \"library\"\n\n[dependencies]\nmath = \"^1.2.0\"\n", edited);
    }

    /// <remarks>The key names the package, and how it is spelled is the author's business rather than ours.</remarks>
    [Fact]
    public void WithDependency_RewritesAnExistingEntry_KeepingItsKeyAndItsComment()
    {
        var edited = Edit("[dependencies]\nMath   =   \"^1.0\"  # pinned deliberately\n", "math", "^2.0");

        Assert.Equal("[dependencies]\nMath = \"^2.0\"  # pinned deliberately\n", edited);
    }

    [Fact]
    public void WithDependency_WritesTheTableForm_ForADevelopmentDependency()
    {
        var edited = Edit("[dependencies]\n", "runit", "^0.4", isDevelopmentOnly: true);

        Assert.Equal("[dependencies]\nrunit = { version = \"^0.4\", dev = true }\n", edited);
    }

    [Fact]
    public void WithDependency_RewritesADevelopmentEntry_BackToAPlainRequirement()
    {
        var edited = Edit("[dependencies]\nrunit = { version = \"^0.4\", dev = true }\n", "runit", "^0.5");

        Assert.Equal("[dependencies]\nrunit = \"^0.5\"\n", edited);
    }

    [Fact]
    public void WithDependency_QuotesAScopedKey_AndFindsItAgain()
    {
        var added = Edit("[dependencies]\n", "alternativelua/tether", "^0.3");
        Assert.Equal("[dependencies]\n\"alternativelua/tether\" = \"^0.3\"\n", added);

        Assert.Equal("[dependencies]\n\"alternativelua/tether\" = \"^0.4\"\n", Edit(added, "alternativelua/tether", "^0.4"));
    }

    /// <remarks>A comment written just above the next table heads what follows it, so an entry goes above it.</remarks>
    [Fact]
    public void WithDependency_AddsAboveACommentTrailingTheTable()
    {
        var edited = Edit("[dependencies]\nmath = \"^1.0\"\n\n# and some day, serio\n\n[realms]\n", "geometry", "^1.0");

        Assert.Equal("[dependencies]\nmath = \"^1.0\"\ngeometry = \"^1.0\"\n\n# and some day, serio\n\n[realms]\n", edited);
    }

    [Fact]
    public void WithDependency_KeepsCarriageReturns_WhereTheManifestHasThem()
    {
        var edited = Edit("[dependencies]\r\nmath = \"^1.0\"\r\n", "serio", "^2.0");

        Assert.Equal("[dependencies]\r\nmath = \"^1.0\"\r\nserio = \"^2.0\"\r\n", edited);
    }

    /// <remarks>An array of tables and a table nested under another are not the <c>[dependencies]</c> table.</remarks>
    [Fact]
    public void WithDependency_IgnoresATableThatOnlyLooksLikeTheDependencyTable()
    {
        var edited = Edit("[[dependencies]]\nname = \"math\"\n", "math", "^1.0");

        Assert.Equal("[[dependencies]]\nname = \"math\"\n\n[dependencies]\nmath = \"^1.0\"\n", edited);
    }

    [Fact]
    public void WithDependency_ReportsAnEntryThatDoesNotEndOnItsOwnLine()
    {
        var manifest = "[dependencies]\nmath = { version = \"^1.0\",\n  dev = true }\n";

        var edited = ManifestEditor.WithDependency(
            manifest,
            PackageName.Parse("math"),
            VersionRequirement.Parse("^2.0"),
            false,
            out var diagnostics
        );

        Assert.Null(edited);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'math'", diagnostic.Message);
        Assert.Equal(2, diagnostic.Line);
    }

    /// <remarks>The point of editing the text is that the result is a manifest, so it has to read back as one.</remarks>
    [Fact]
    public void WithDependency_ProducesAManifestThatReadsBack()
    {
        using var fixture = new PackageIndexFixture();
        fixture.WriteProject("math = \"^1.0\"");

        var edited = Edit(fixture.ReadManifest(), "runit", "^0.4", isDevelopmentOnly: true);
        File.WriteAllText(Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName), edited);

        var config = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal(2, config!.Dependencies.Count);
        Assert.True(config.Dependencies[PackageName.Parse("runit")].IsDevelopmentOnly);
        Assert.Equal(VersionRequirement.Parse("^1.0"), config.Dependencies[PackageName.Parse("math")].VersionRequirement);
    }

    private static string Edit(string manifest, string name, string requirement, bool isDevelopmentOnly = false)
    {
        var edited = ManifestEditor.WithDependency(
            manifest,
            PackageName.Parse(name),
            VersionRequirement.Parse(requirement),
            isDevelopmentOnly,
            out var diagnostics
        );

        Assert.Empty(diagnostics);
        Assert.NotNull(edited);
        return edited;
    }
}
