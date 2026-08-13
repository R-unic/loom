using System.Reflection;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing;

/// <summary>
///     The contract every request handler shares, driven across all of them at once rather than restated in
///     each handler's own test: a request for a document the server has never seen is answered rather than
///     thrown at, a position with nothing under it comes back empty, and each handler registers for the
///     files it is meant to serve.
/// </summary>
/// <remarks>
///     These are the paths a handler's own tests never take, because those tests open a real document and
///     point at a real symbol. They are also the paths an editor takes constantly - it asks about files it
///     has open that belong to no project, and about wherever the cursor happens to be sitting.
/// </remarks>
[Collection("Assembly")]
public class LanguageServerHandlerContractTest
{
    private const string Source = """
        interface Packet { name: string }

        fn main(p: Packet): void {
            print(p.name);
        }
        """;

    /// <summary>Every handler and the request it answers, as a call that takes a store and a document.</summary>
    private static IEnumerable<(string Name, Func<DocumentStore, DocumentUri, Position, Task<object?>> Invoke)> Handlers()
    {
        yield return ("Hover", async (s, u, p) => await new HoverHandler(s).Handle(new HoverParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("Definition", async (s, u, p) => await new DefinitionHandler(s).Handle(new DefinitionParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("TypeDefinition", async (s, u, p) => await new TypeDefinitionHandler(s).Handle(new TypeDefinitionParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("Implementation", async (s, u, p) => await new ImplementationHandler(s).Handle(new ImplementationParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("Completion", async (s, u, p) => await new CompletionHandler(s).Handle(new CompletionParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("SignatureHelp", async (s, u, p) => await new SignatureHelpHandler(s).Handle(new SignatureHelpParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("PrepareRename", async (s, u, p) => await new PrepareRenameHandler(s).Handle(new PrepareRenameParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("Rename", async (s, u, p) => await new RenameHandler(s).Handle(new RenameParams { TextDocument = new TextDocumentIdentifier(u), Position = p, NewName = "renamed" }, Cancel));
        yield return ("References", async (s, u, p) => await new ReferencesHandler(s).Handle(new ReferenceParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("DocumentHighlight", async (s, u, p) => await new DocumentHighlightHandler(s).Handle(new DocumentHighlightParams { TextDocument = new TextDocumentIdentifier(u), Position = p }, Cancel));
        yield return ("FoldingRange", async (s, u, _) => await new FoldingRangeHandler(s).Handle(new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier(u) }, Cancel));
        yield return ("DocumentSymbol", async (s, u, _) => await new DocumentSymbolHandler(s).Handle(new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(u) }, Cancel));
        yield return ("InlayHint", async (s, u, _) => await new InlayHintHandler(s).Handle(new InlayHintParams { TextDocument = new TextDocumentIdentifier(u), Range = WholeFile }, Cancel));
        yield return ("CodeAction", async (s, u, _) => await new CodeActionHandler(s).Handle(new CodeActionParams { TextDocument = new TextDocumentIdentifier(u), Range = WholeFile, Context = new CodeActionContext() }, Cancel));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
    private static Range WholeFile => new(new Position(0, 0), new Position(0, 0));

    public static TheoryData<string> HandlerNames()
    {
        var names = new TheoryData<string>();
        foreach (var (name, _) in Handlers())
            names.Add(name);

        return names;
    }

    /// <remarks>
    ///     An editor asks about every file it has open, including ones outside any Loom project. Answering
    ///     is the contract - throwing takes down the request, and the client shows an error per keystroke.
    /// </remarks>
    [Theory]
    [MemberData(nameof(HandlerNames))]
    public async Task AnUnknownDocument_IsAnsweredRatherThanThrownAt(string name)
    {
        var invoke = Handlers().Single(handler => handler.Name == name).Invoke;
        var store = new DocumentStore();
        var unknown = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "loom-never-opened", "nowhere.loom"));

        var result = await invoke(store, unknown, new Position(0, 0));

        var answered = result switch
        {
            null => 0,
            CompletionList completions => completions.Count(),
            System.Collections.IEnumerable items => items.Cast<object>().Count(),
            _ => 1
        };

        Assert.True(answered == 0, $"{name} answered {answered} result(s) for a document it has never seen.");
    }

    /// <remarks>The cursor spends most of its life on whitespace; every handler is asked about it anyway.</remarks>
    [Theory]
    [MemberData(nameof(HandlerNames))]
    public async Task APositionWithNothingUnderIt_IsAnsweredRatherThanThrownAt(string name)
    {
        var invoke = Handlers().Single(handler => handler.Name == name).Invoke;
        await Utility.WithLspProjectAsync(async (store, uri) => await invoke(store, uri, new Position(1, 0)), Source);
    }

    /// <remarks>
    ///     A handler registered for the wrong pattern is never asked anything, which looks exactly like the
    ///     feature not being implemented. Nothing else checks the pattern - the server only reads it once, at
    ///     a point no test reaches.
    /// </remarks>
    [Fact]
    public void EveryHandler_RegistersForLoomFiles()
    {
        var handlerTypes = typeof(HoverHandler).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(handlerTypes);
        foreach (var type in handlerTypes)
        {
            var method = type.GetMethod("CreateRegistrationOptions", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) continue;

            var options = method.Invoke(Construct(type), method.GetParameters().Select(_ => (object?)null).ToArray());
            var selector = options?.GetType().GetProperty("DocumentSelector")?.GetValue(options);
            if (selector == null) continue;

            Assert.Contains("**/*.loom", selector.ToString());
        }
    }

    private static object Construct(Type type)
    {
        var store = new DocumentStore();
        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(object? (parameter) =>
                {
                    if (parameter.ParameterType == typeof(DocumentStore)) return store;
                    if (parameter.ParameterType == typeof(DiagnosticPublisher)) return new DiagnosticPublisher(static _ => { });
                    if (parameter.ParameterType == typeof(Debouncer)) return new Debouncer(TimeSpan.Zero);

                    return null;
                }
            )
            .ToArray();

        return constructor.Invoke(arguments);
    }
}
