using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     An <c>event</c> declaration, whether it is a member of an interface body or stands at module scope.
///     Both are reached the same way - by name, off a receiver or off the module - so both are the same
///     kind of member.
/// </summary>
public sealed class EventSymbol(EventDeclaration declaration, List<AttributeSymbol>? attributes = null)
    : PropertySymbol(declaration, null, attributes ?? [])
{
    public override SymbolKind Kind => SymbolKind.Event;
}
