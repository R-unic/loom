using System.Text;
using Loom.Tools;

Console.OutputEncoding = Encoding.UTF8;

var arguments = args;
var toolName = arguments.ElementAtOrDefault(0);
var tools = new Dictionary<string, Action>
{
    ["generate-ast-snapshots"] = () => AstTools.GenerateAstSnapshots(arguments),
    ["ast"] = () =>
    {
        var filePath = arguments.ElementAtOrDefault(1);
        if (string.IsNullOrEmpty(filePath))
        {
            Console.WriteLine("Usage: loomtools ast <file>");
            return;
        }

        Console.WriteLine(AstTools.GetAstString(filePath));
    },
    ["profile"] = () => ProfileTools.Profile(arguments)
};

if (!string.IsNullOrEmpty(toolName) && tools.TryGetValue(toolName, out var runTool))
{
    runTool();
    return;
}

Console.WriteLine($"Usage: loomtools [{string.Join('|', tools.Keys)}]");