using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Annotates each top-level declaration with how much of the project depends on it. Counting is deferred
///     to the resolve request: a lens is asked for the whole file at once, and answering every one eagerly
///     would run a project-wide search per declaration to fill in numbers for lines that are not on screen.
/// </summary>
public sealed class CodeLensHandler(DocumentStore documents, ServerSettings settings) : CodeLensHandlerBase
{
    private const string UriKey = "loomUri";
    private const string OffsetKey = "loomOffset";
    private const string KindKey = "loomKind";
    private const string ReferencesKind = "references";
    private const string ImplementationsKind = "implementations";

    public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken cancellationToken)
    {
        if (!settings.CodeLensEnabled || !documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<CodeLensContainer?>(null);

        var lenses = new List<CodeLens>();
        foreach (var target in CodeLenses.In(state.File))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.CodeLensReferences)
                lenses.Add(Lens(target, request.TextDocument.Uri, ReferencesKind));

            // a trait is the one declaration whose implementations are written where its own name never
            // appears, so the count is worth showing beside the references that do name it
            if (settings.CodeLensImplementations && target.IsTrait)
                lenses.Add(Lens(target, request.TextDocument.Uri, ImplementationsKind));
        }

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    public override Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
    {
        if (Resolve(request) is not var (target, state, kind))
            return Task.FromResult(request);

        // both counts walk state.Unit.AnalyzedModules, which a concurrent recompile clears and repopulates
        string title;
        lock (state.CompilationLock)
        {
            title = kind == ImplementationsKind
                ? CodeLenses.Describe(CodeLenses.ImplementationCount(target, state.Unit), "implementation")
                : CodeLenses.Describe(CodeLenses.ReferenceCount(target, state.Unit, cancellationToken), "reference");
        }

        // no command name: the lens is a count to read, and a client that cannot navigate from one should
        // still show the number rather than a link that does nothing
        return Task.FromResult(request with { Command = new Command { Name = "", Title = title } });
    }

    private static CodeLens Lens(CodeLensTarget target, DocumentUri uri, string kind) =>
        new()
        {
            Range = Conversion.ToRange(target.Name.GetLocation()),
            Data = new JObject { [UriKey] = uri.ToString(), [OffsetKey] = target.Name.Span.Position, [KindKey] = kind }
        };

    private (CodeLensTarget Target, DocumentState State, string Kind)? Resolve(CodeLens lens)
    {
        if (lens.Data is not { } data
            || data[UriKey]?.Value<string>() is not { } uri
            || data[OffsetKey]?.Value<int>() is not { } offset
            || data[KindKey]?.Value<string>() is not { } kind)
            return null;

        if (!documents.TryGetState(DocumentUri.Parse(uri), out var state))
            return null;

        // found by where the name starts rather than by index: the file may have been edited since the lens
        // was handed out, and a declaration that has moved is better left unresolved than answered wrongly
        var target = CodeLenses.In(state.File).FirstOrDefault(candidate => candidate.Name.Span.Position == offset);
        return target == null ? null : (target, state, kind);
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"), ResolveProvider = true };
}
