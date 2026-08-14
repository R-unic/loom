using Loom.Core.Parsing.AST;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

/// <summary>
///     Expand-selection: the chain of ever-larger pieces of syntax around a position. The editor's own answer
///     is a bracket scan, which cannot tell the parentheses of a call from those grouping an expression, and
///     stops at the first bracket either way - so selecting a whole condition, arm, or declaration takes as
///     many presses as it has punctuation.
/// </summary>
public sealed class SelectionRangeHandler(DocumentStore documents) : SelectionRangeHandlerBase
{
    public override Task<Container<SelectionRange>?> Handle(SelectionRangeParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<Container<SelectionRange>?>(null);

        var text = state.File.SourceFile.SourceText;
        var ranges = new List<SelectionRange>();
        foreach (var position in request.Positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ChainAt(state.File.Tree, IncrementalText.ToOffset(text, position)) is { } chain)
                ranges.Add(chain);
        }

        return Task.FromResult<Container<SelectionRange>?>(new Container<SelectionRange>(ranges));
    }

    /// <summary>
    ///     The node under <paramref name="offset" /> and everything containing it, innermost first. A wrapper
    ///     that spans exactly what it wraps is left out: the protocol wants each step to be a step, and an
    ///     entry the same size as the one before it costs the user a keypress that does nothing.
    /// </summary>
    private static SelectionRange? ChainAt(Tree tree, int offset)
    {
        var node = NodeFinder.FindAt(tree, offset);
        if (node == null)
            return null;

        var spans = new List<LspRange>();
        for (var current = node; current != null; current = current.Parent)
        {
            var span = Conversion.ToRange(current.LocationSpan);
            if (spans.Count == 0 || spans[^1] != span)
                spans.Add(span);
        }

        // built from the outside in, since each entry holds the one containing it rather than the other way.
        // The outermost has no parent - the protocol says so, and the model's property is simply not nullable
        SelectionRange? range = null;
        for (var i = spans.Count - 1; i >= 0; i--)
            range = new SelectionRange { Range = spans[i], Parent = range! };

        return range;
    }

    protected override SelectionRangeRegistrationOptions CreateRegistrationOptions(SelectionRangeCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
