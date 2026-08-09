namespace Loom.Core.Parsing.AST;

/// <summary>
///     A statement that forwards another module's exports — <c>export { a } from "./m"</c> or
///     <c>export * from "./m"</c>. Every implementation is a <see cref="Statement" />, so the module graph
///     can resolve the specifier of one without knowing which form it took.
/// </summary>
public interface IReExport
{
    Literal? ModuleSpecifier { get; }
    string? ModulePath { get; }
    bool IsTypeOnly { get; }

    /// <summary>False for an <see cref="ExportList" /> that names no module, which exports local names instead.</summary>
    bool IsReExport { get; }
}
