using Loom.TypeGenerator;
using Loom.TypeGenerator.ApiTypes;
using Loom.TypeGenerator.Generators;
using ValueType = Loom.TypeGenerator.ApiTypes.ValueType;

namespace Loom.Testing;

[Collection("Assembly")]
public class ClassGeneratorTest
{
    [Fact]
    public void GetParameterNames_RenamesDuplicate()
    {
        var parameters = new[]
        {
            new Parameter { Name = "part" },
            new Parameter { Name = "other" },
            new Parameter { Name = "part" }
        };

        var names = ClassGenerator.GetParameterNames(parameters);
        Assert.Equal(["part", "other", "part0"], names);
    }

    [Fact]
    public void SafeReturnType_MultiReturn_EmitsTupleSyntax()
    {
        var returnType = ClassUtility.SafeReturnType([new ValueType { Name = "string" }, new ValueType { Name = "number" }]);
        Assert.Equal("(string, number)", returnType);
    }

    [Fact]
    public void SafeReturnType_ThreeValues_EmitsTupleSyntax()
    {
        var returnType = ClassUtility.SafeReturnType(
            [new ValueType { Name = "string" }, new ValueType { Name = "number" }, new ValueType { Name = "bool" }]
        );

        Assert.Equal("(string, number, bool)", returnType);
    }

    [Fact]
    public void SafeReturnType_SingleTupleReturn_StaysUnknown()
    {
        var returnType = ClassUtility.SafeReturnType([new ValueType { Name = "Tuple" }]);
        Assert.Equal("unknown", returnType);
    }

    [Fact]
    public void SafeReturnType_SingleValue_ReturnsThatType()
    {
        var returnType = ClassUtility.SafeReturnType([new ValueType { Name = "string" }]);
        Assert.Equal("string", returnType);
    }

    [Fact]
    public void SafeReturnType_NoValues_ReturnsVoid()
    {
        var returnType = ClassUtility.SafeReturnType(null);
        Assert.Equal("void", returnType);
    }

    [Fact]
    public void GenerateFunction_MultiReturn_EmitsTupleReturnType()
    {
        var generator = new ClassGenerator("test.loom", new ReflectionMetadataReader("<a></a>"), [], "None");
        var rbxClass = new Class { Name = "Foo", Superclass = Constants.RootClassName, Members = [], Description = "" };
        var function = new Function
        {
            MemberType = "Function",
            Name = "DoTuple",
            Description = "",
            Parameters = [],
            ReturnType = [new ValueType { Name = "string" }, new ValueType { Name = "number" }]
        };

        generator.GenerateFunction(function, rbxClass);
        Assert.Contains(": (string, number);", generator.Stream.ToString());
    }

    [Fact]
    public void GenerateCallback_MultiReturn_NotSkipped()
    {
        var generator = new ClassGenerator("test.loom", new ReflectionMetadataReader("<a></a>"), [], "None");
        var rbxClass = new Class { Name = "Foo", Superclass = Constants.RootClassName, Members = [], Description = "" };
        var callback = new Callback
        {
            MemberType = "Callback",
            Name = "OnTuple",
            Description = "",
            Parameters = [],
            ReturnType = [new ValueType { Name = "string" }, new ValueType { Name = "number" }]
        };

        generator.GenerateCallback(callback, rbxClass);
        var output = generator.Stream.ToString();
        Assert.NotEmpty(output);
        Assert.Contains(": (string, number);", output);
    }

    [Fact]
    public void GenerateFunction_TupleTypedParameter_EmitsRestParameter()
    {
        var generator = new ClassGenerator("test.loom", new ReflectionMetadataReader("<a></a>"), [], "None");
        var rbxClass = new Class { Name = "Foo", Superclass = Constants.RootClassName, Members = [], Description = "" };
        var function = new Function
        {
            MemberType = "Function",
            Name = "Print",
            Description = "",
            ReturnType = null,
            Parameters = [new Parameter { Name = "data", Type = new ValueType { Name = "Tuple" }, Default = "" }]
        };

        generator.GenerateFunction(function, rbxClass);
        Assert.Contains("(..data: unknown[])", generator.Stream.ToString());
    }

    [Fact]
    public void GeneratedTupleReturnAndRestParameter_RoundTrips_ThroughRealCompiler()
    {
        var generator = new ClassGenerator("test.loom", new ReflectionMetadataReader("<a></a>"), [], "None");
        var rbxClass = new Class { Name = "Foo", Superclass = Constants.RootClassName, Members = [], Description = "" };
        var tupleFunction = new Function
        {
            MemberType = "Function",
            Name = "DoTuple",
            Description = "",
            Parameters = [],
            ReturnType = [new ValueType { Name = "string" }, new ValueType { Name = "number" }]
        };

        var restFunction = new Function
        {
            MemberType = "Function",
            Name = "Print",
            Description = "",
            ReturnType = null,
            Parameters = [new Parameter { Name = "data", Type = new ValueType { Name = "Tuple" }, Default = "" }]
        };

        generator.GenerateFunction(tupleFunction, rbxClass);
        generator.GenerateFunction(restFunction, rbxClass);

        var source = $"interface Foo {{\n{generator.Stream}\n}}";
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }
}
