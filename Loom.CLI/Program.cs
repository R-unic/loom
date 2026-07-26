using System.Text;
using Loom.Config;
using Loom.Core;

Console.OutputEncoding = Encoding.UTF8;

// DiagnosticBag.FailFast = false;

var directory = args.ElementAtOrDefault(0) ?? ".";
var loomConfig = ConfigReader.LocateFromDirectory(directory);
if (loomConfig == null)
    throw new ArgumentException($"Could not locate Loom configuration file in directory '{directory}'.");

var compilationUnit = new CompilationUnit(loomConfig);
var result = compilationUnit.Compile();
var debugInfo = result.Files
    .Where(f => !f.SourceFile.IsDeclaration)
    .Select(f => f.GetDebugInfo(false, debugDiagnostics: loomConfig.Debug));

Console.WriteLine(string.Join(Environment.NewLine, debugInfo));