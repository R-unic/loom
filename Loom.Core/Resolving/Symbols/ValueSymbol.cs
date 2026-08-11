using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>A name that stands for a runtime value, and so is looked up in a scope's variable table.</summary>
public abstract class ValueSymbol(Node declaration, string name, bool isMutable = false) : Symbol(declaration, name, isMutable);
