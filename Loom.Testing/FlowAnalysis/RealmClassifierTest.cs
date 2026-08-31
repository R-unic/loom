using Loom.TypeGenerator;
using Loom.TypeGenerator.ApiTypes;
using Loom.TypeGenerator.Generators;
using ValueType = Loom.TypeGenerator.ApiTypes.ValueType;

namespace Loom.Testing.FlowAnalysis;

[Collection("Assembly")]
public class RealmClassifierTest
{
    private static readonly string _dataFile = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "Loom.TypeGenerator", "Data", "realm.toml"
    );

    private static Class ClassNamed(string name) => new() { Name = name, Members = [], Superclass = "<<<ROOT>>>" };

    private static Property PropertyNamed(string name) => new()
    {
        Name = name,
        MemberType = "Property",
        Tags = null,
        Security = "{\"Read\":\"None\",\"Write\":\"None\"}"
    };

    [Fact]
    public void TheDataFileIsFoundAndParsed()
    {
        Assert.True(File.Exists(_dataFile), $"expected the dataset at {_dataFile}");

        var classifier = new RealmClassifier(_dataFile);
        Assert.Equal("server", classifier.ClassAttribute(ClassNamed("DataStoreService")));
        Assert.Equal("client", classifier.MemberAttribute(ClassNamed("Players"), PropertyNamed("LocalPlayer")));
    }

    [Theory]
    [InlineData("DataStoreService", "server")]
    [InlineData("MessagingService", "server")]
    [InlineData("Players", null)]
    [InlineData("DataStore", null)]
    public void ClassifiesWholeClasses(string className, string? expected)
    {
        var classifier = new RealmClassifier(_dataFile);

        Assert.Equal(expected, classifier.ClassAttribute(ClassNamed(className)));
    }

    [Theory]
    [InlineData("Players", "LocalPlayer", "client")]
    [InlineData("RunService", "RenderStepped", "client")]
    [InlineData("RunService", "PreRender", "client")]
    [InlineData("RunService", "Heartbeat", null)]
    [InlineData("Players", "GetPlayers", null)]
    [InlineData("RemoteEvent", "FireServer", "client")]
    [InlineData("RemoteEvent", "OnClientEvent", "client")]
    [InlineData("RemoteEvent", "FireClient", "server")]
    [InlineData("RemoteEvent", "FireAllClients", "server")]
    [InlineData("RemoteEvent", "OnServerEvent", "server")]
    [InlineData("UnreliableRemoteEvent", "FireServer", "client")]
    [InlineData("UnreliableRemoteEvent", "FireClient", "server")]
    [InlineData("RemoteFunction", "InvokeServer", "client")]
    [InlineData("RemoteFunction", "OnClientInvoke", "client")]
    [InlineData("RemoteFunction", "InvokeClient", "server")]
    [InlineData("RemoteFunction", "OnServerInvoke", "server")]
    public void ClassifiesIndividualMembers(string className, string memberName, string? expected)
    {
        var classifier = new RealmClassifier(_dataFile);

        Assert.Equal(expected, classifier.MemberAttribute(ClassNamed(className), PropertyNamed(memberName)));
    }

    [Fact]
    public void AMemberOfARestrictedClassNeedsNoEntryOfItsOwn()
    {
        var classifier = new RealmClassifier(_dataFile);

        Assert.Null(classifier.MemberAttribute(ClassNamed("DataStoreService"), PropertyNamed("GetDataStore")));
        Assert.Equal("server", classifier.ClassAttribute(ClassNamed("DataStoreService")));
    }

    [Fact]
    public void WithoutADataFileNothingIsRestricted()
    {
        var classifier = new RealmClassifier(Path.Combine(Path.GetTempPath(), "does-not-exist.toml"));

        Assert.Null(classifier.ClassAttribute(ClassNamed("DataStoreService")));
        Assert.Null(classifier.MemberAttribute(ClassNamed("Players"), PropertyNamed("LocalPlayer")));
    }

    [Fact]
    public void TheGeneratorWritesTheClassAttributeOntoEveryMemberOfARestrictedClass()
    {
        var emitted = Emit("DataStoreService", PropertyNamed("GetDataStore"));

        Assert.Contains("server", emitted);
    }

    [Fact]
    public void TheGeneratorWritesTheMemberAttributeOntoARestrictedMemberOfAnUnrestrictedClass()
    {
        var emitted = Emit("Players", PropertyNamed("LocalPlayer"));

        Assert.Contains("client", emitted);
    }

    [Fact]
    public void TheGeneratorLeavesAnUnrestrictedMemberAlone()
    {
        var emitted = Emit("Players", PropertyNamed("GetPlayers"));

        Assert.DoesNotContain("server", emitted);
        Assert.DoesNotContain("client", emitted);
    }

    private static string Emit(string className, Property property)
    {
        var rbxClass = ClassNamed(className);
        property.ValueType = new ValueType { Name = "string", Category = "Primitive" };

        var generator = new ClassGenerator(
            Path.Combine(Path.GetTempPath(), "loom-generator-test.loom"),
            new ReflectionMetadataReader("<roblox/>"),
            [],
            "None"
        );

        generator.GenerateProperty(property, rbxClass);
        return generator.Stream.ToString();
    }
}
