using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed record VisibleSymbol(string Name, CompletionItemKind Kind, string TypeDescription)
{
    public TextSpan Scope { get; init; }
    public bool IsTypeSymbol { get; init; }
    public bool IsLocal { get; init; }
}
