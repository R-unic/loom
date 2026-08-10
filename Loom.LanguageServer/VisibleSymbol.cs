using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     One name a completion request may offer, and everything needed to decide whether it belongs at the
///     cursor. The detail and documentation are deferred: a project sees thousands of visible names on every
///     keystroke and reads at most a handful of them, so describing one costs nothing until it is asked for.
/// </summary>
public sealed record VisibleSymbol(string Name, CompletionItemKind Kind)
{
    private static readonly Func<string> _noDetail = () => "";
    private static readonly Func<string?> _noDocumentation = () => null;

    public TextSpan Scope { get; init; }
    public bool IsTypeSymbol { get; init; }
    public bool IsLocal { get; init; }

    /// <summary>The signature shown beside the label, resolved only when the client asks about this item.</summary>
    public Func<string> Detail { get; init; } = _noDetail;

    /// <summary>The markdown shown in the item's documentation pane, resolved only when the client asks about this item.</summary>
    public Func<string?> Documentation { get; init; } = _noDocumentation;
}
