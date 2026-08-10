using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

/// <summary>
///     Describes the call the cursor is inside: which signature it will match, which parameter is being
///     written, and what that parameter is for. An overload set is one type made of several function shapes,
///     so the whole set is offered and the one the arguments so far fit is the active one.
/// </summary>
public sealed class SignatureHelpHandler(DocumentStore documents) : SignatureHelpHandlerBase
{
    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<SignatureHelp?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            if (CallSiteFinder.FindAt(state.File.Tree, offset) is not { } callSite)
                return Task.FromResult<SignatureHelp?>(null);

            return Task.FromResult(Describe(callSite, state));
        }
        catch (Exception)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"),
            TriggerCharacters = new Container<string>("(", ","),
            // ')' closes the inner call of a nested one, which puts the cursor back in the outer call's
            // argument list - the popup has to be recomputed there rather than dismissed
            RetriggerCharacters = new Container<string>(")")
        };

    private static SignatureHelp? Describe(CallSite callSite, DocumentState state)
    {
        var callee = callSite.Invocation.Expression;
        var semanticModel = state.File.SemanticModel;
        var symbols = SymbolsOf(callee, semanticModel);
        var signatures = DeclarationDisplay.CallSignatures(symbols, TypeOf(semanticModel, callee), NameOf(callee, symbols.FirstOrDefault()));
        if (signatures.Count == 0)
            return null;

        var documentation = DocumentationBlock.Parse(symbols.FirstOrDefault()?.Declaration.Documentation);
        var active = ActiveSignature(signatures, callSite.ArgumentIndex);

        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signatures.Select(signature => ToSignatureInformation(signature, documentation))),
            ActiveSignature = active,
            ActiveParameter = Math.Min(callSite.ArgumentIndex, Math.Max(0, signatures[active].Parameters.Count - 1))
        };
    }

    /// <summary>The first overload that could still accept this many arguments, falling back to the first when none can.</summary>
    private static int ActiveSignature(IReadOnlyList<SignatureDisplay> signatures, int argumentIndex)
    {
        for (var index = 0; index < signatures.Count; index++)
            if (signatures[index].Accepts(argumentIndex + 1))
                return index;

        return 0;
    }

    private static SignatureInformation ToSignatureInformation(SignatureDisplay signature, DocumentationBlock documentation) =>
        new()
        {
            Label = signature.Label,
            Documentation = Markdown(documentation.Summary),
            Parameters = new Container<ParameterInformation>(
                signature.Parameters.Select(parameter => new ParameterInformation
                    {
                        // offsets rather than the label text, so a parameter whose rendering repeats elsewhere
                        // in the signature (two of the same type, say) still highlights the right one
                        Label = new ParameterInformationLabel((parameter.Start, parameter.End)),
                        Documentation = Markdown(parameter.Documentation ?? documentation.ForParameter(parameter.Name))
                    }
                )
            )
        };

    private static StringOrMarkupContent? Markdown(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : new StringOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = text });

    /// <summary>
    ///     Every declaration the callee names. More than one only for an overload set, whose shapes the type
    ///     checker merged into a single type - each shape's parameter names are on its own declaration.
    /// </summary>
    private static IReadOnlyList<Symbol> SymbolsOf(Expression callee, SemanticModel semanticModel)
    {
        if (semanticModel.GetPropertySymbols(callee) is { Count: > 0 } properties)
            return properties;

        var symbol = semanticModel.GetSymbol(callee) ?? semanticModel.GetNamespaceMemberSymbol(callee);
        return symbol == null ? [] : [symbol];
    }

    /// <summary>The name to render the signature under, which for a member is the member's rather than the receiver's.</summary>
    private static string NameOf(Expression callee, Symbol? symbol) =>
        callee switch
        {
            QualifiedName { Names: [.., var last] } => last.Name.Text,
            PropertyAccess { Names: [.., var last] } => last.Name.Text,
            _ => symbol?.Name ?? callee.ToString()
        };

    private static Type? TypeOf(SemanticModel semanticModel, Node node)
    {
        try
        {
            return semanticModel.GetType(node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
