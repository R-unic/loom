using System.Collections.Concurrent;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class SemanticTokensHandler(DocumentStore documents) : SemanticTokensHandlerBase
{
    /// <summary>
    ///     One token document per open file, kept so that a client asking for a delta is answered with the
    ///     edits rather than the whole file again. The document is what holds the previous result to diff
    ///     against; a fresh one every request would make every delta a full response wearing a delta's name.
    /// </summary>
    private readonly ConcurrentDictionary<DocumentUri, SemanticTokensDocument> _documents = [];

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.GetOrAdd(identifier.TextDocument.Uri, _ => new SemanticTokensDocument(SemanticTokenClassifier.Legend)));

    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(identifier.TextDocument.Uri, out var state))
        {
            // nothing to tokenize means the document closed or was never in a project, and either way the
            // tokens kept to diff against describe text nobody is looking at any more
            _documents.TryRemove(identifier.TextDocument.Uri, out _);
            return Task.CompletedTask;
        }

        var file = state.File.SourceFile;
        foreach (var classified in SemanticTokenClassifier.Of(state.File))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Push(builder, file, classified);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Pushes one token, a line at a time. The protocol has no way to describe a token that spans lines -
    ///     a token is a line, a start column and a length - and Loom has three that can: a block comment, an
    ///     interpolated string, and a string literal holding an escaped newline.
    /// </summary>
    private static void Push(SemanticTokensBuilder builder, SourceFile file, ClassifiedToken classified)
    {
        var span = classified.Token.Span;
        var text = file.SourceText;
        var line = file.GetLineFromPosition(span.Position) - 1;
        var character = file.GetCharacterFromPosition(span.Position);
        var start = span.Position;

        for (var position = span.Position; position < span.End && position < text.Length; position++)
        {
            if (text[position] != '\n')
                continue;

            PushSegment(builder, classified, line, character, LengthWithoutLineBreak(text, start, position));
            line++;
            character = 0;
            start = position + 1;
        }

        PushSegment(builder, classified, line, character, span.End - start);
    }

    private static void PushSegment(SemanticTokensBuilder builder, ClassifiedToken classified, int line, int character, int length)
    {
        if (length > 0)
            builder.Push(line, character, length, classified.Type, classified.Modifiers);
    }

    /// <summary>The length of the segment up to <paramref name="lineFeed" />, leaving out a carriage return the line break brought with it.</summary>
    private static int LengthWithoutLineBreak(string text, int start, int lineFeed)
    {
        var end = lineFeed > start && text[lineFeed - 1] == '\r' ? lineFeed - 1 : lineFeed;
        return end - start;
    }

    /// <summary>
    ///     Delta is off: OmniSharp's own <c>SemanticTokensDocument.GetSemanticTokensEdits</c> computes its
    ///     common-prefix and common-suffix lengths independently, and a run of identical adjacent tokens
    ///     spanning the edit lets the two scans overlap, driving the slice length negative and throwing
    ///     <c>ArgumentOutOfRangeException</c> out of library code this project does not own. Unadvertised, a
    ///     client never sends <c>textDocument/semanticTokens/full/delta</c> and always gets the full array
    ///     instead - correct either way, just not incremental.
    /// </summary>
    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"),
            Legend = SemanticTokenClassifier.Legend,
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = true
        };
}
