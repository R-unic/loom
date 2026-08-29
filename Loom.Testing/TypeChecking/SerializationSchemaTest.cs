using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Serialization;

namespace Loom.Testing;

[Collection("Assembly")]
public partial class SerializationSchemaTest
{
    private static SerializationSchema GetSchema(string source, string interfaceName = "MyData")
    {
        var (_, semanticModel, flowAnalyzer) = Utility.FlowAnalyze(source);
        var result = new Core.TypeChecking.TypeChecker(semanticModel, flowAnalyzer).Check();
        Utility.AssertNoErrors(result.Diagnostics);

        var schema = semanticModel.SerializationSchemas
            .FirstOrDefault(pair => pair.Key.Name == interfaceName)
            .Value;

        Assert.NotNull(schema);
        return schema;
    }




}
