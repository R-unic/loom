using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>A name that can be written in type position, and so is looked up in a scope's type table.</summary>
public abstract class TypeSymbol(Node declaration, string name) : Symbol(declaration, name);
