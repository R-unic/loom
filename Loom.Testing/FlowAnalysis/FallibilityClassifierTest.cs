using Loom.TypeGenerator;
using Loom.TypeGenerator.ApiTypes;
using Loom.TypeGenerator.Generators;
using ValueType = Loom.TypeGenerator.ApiTypes.ValueType;

namespace Loom.Testing.FlowAnalysis;

[Collection("Assembly")]
public class FallibilityClassifierTest
{
    private static readonly string _dataFile = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "Loom.TypeGenerator", "Data", "fallible.toml"
    );

    private static Class ClassNamed(string name) => new() { Name = name, Members = [], Superclass = "<<<ROOT>>>" };

    private static Function FunctionNamed(string name, params string[] tags) =>
        new() { Name = name, MemberType = "Function", Tags = tags.Length == 0 ? null : [.. tags], Parameters = [] };

    [Fact]
    public void TheDataFileIsFoundAndParsed()
    {
        Assert.True(File.Exists(_dataFile), $"expected the dataset at {_dataFile}");

        var classifier = new FallibilityClassifier(_dataFile);
        Assert.True(classifier.IsFallible(ClassNamed("HttpService"), FunctionNamed("JSONDecode")));
    }

    [Theory]
    [InlineData("GlobalDataStore", "GetAsync", new[] { "Yields" }, true)]
    [InlineData("Instance", "WaitForChild", new[] { "CanYield" }, false)]
    [InlineData("BasePart", "CanCollideWith", new[] { "Yields" }, false)]
    [InlineData("MeshPart", "ApplyMesh", new[] { "Yields" }, false)]
    [InlineData("HttpService", "JSONEncode", new string[0], true)]
    [InlineData("BasePart", "SetNetworkOwner", new string[0], true)]
    [InlineData("Instance", "FindFirstChild", new string[0], false)]
    [InlineData("Instance", "IsA", new string[0], false)]
    public void Classifies(string className, string functionName, string[] tags, bool expected)
    {
        var classifier = new FallibilityClassifier(_dataFile);

        Assert.Equal(expected, classifier.IsFallible(ClassNamed(className), FunctionNamed(functionName, tags)));
    }

    [Fact]
    public void AnUnknownYieldingMethodIsFallible()
    {
        var classifier = new FallibilityClassifier(_dataFile);

        Assert.True(classifier.IsFallible(ClassNamed("FutureService"), FunctionNamed("SomethingNewAsync", "Yields")));
    }

    [Fact]
    public void WithoutADataFileTheSeedStillClassifies()
    {
        var classifier = new FallibilityClassifier(Path.Combine(Path.GetTempPath(), "does-not-exist.toml"));

        Assert.True(classifier.IsFallible(ClassNamed("GlobalDataStore"), FunctionNamed("GetAsync", "Yields")));
        Assert.False(classifier.IsFallible(ClassNamed("Instance"), FunctionNamed("FindFirstChild")));
    }

    [Fact]
    public void TheGeneratorWrapsAFallibleMethodAndMarksIt()
    {
        var emitted = Emit(FunctionNamed("GetAsync", "Yields"));

        Assert.Contains("wraps_errors", emitted);
        Assert.Contains("Result<string, RobloxError>", emitted);
    }

    [Fact]
    public void TheGeneratorLeavesAnInfallibleMethodAlone()
    {
        var emitted = Emit(FunctionNamed("FindFirstChild"));

        Assert.DoesNotContain("wraps_errors", emitted);
        Assert.DoesNotContain("RobloxError", emitted);
    }

    private static string Emit(Function function)
    {
        var rbxClass = ClassNamed("GlobalDataStore");
        function.ReturnType = [new ValueType { Name = "string", Category = "Primitive" }];

        var generator = new ClassGenerator(
            Path.Combine(Path.GetTempPath(), "loom-generator-test.loom"),
            new ReflectionMetadataReader("<roblox/>"),
            [],
            "None"
        );

        generator.GenerateFunction(function, rbxClass);
        return generator.Stream.ToString();
    }
}
