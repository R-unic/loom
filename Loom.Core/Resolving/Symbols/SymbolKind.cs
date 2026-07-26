namespace Loom.Core.Resolving.Symbols;

public enum SymbolKind : byte
{
    Variable,
    Function,
    Parameter,
    Property,
    InjectedPropertyVariable,
    Attribute,
    Type,
    EnumType,
    Interface,
    Trait,
    Event
}