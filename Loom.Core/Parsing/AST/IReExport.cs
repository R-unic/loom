namespace Loom.Core.Parsing.AST;

/// <summary>
///     A statement that forwards another module's exports — <c>export { a } from "./m"</c> or
///     <c>export * from "./m"</c>. Every implementation is a <see cref="Statement" />, so the module graph
///     can resolve the specifier of one without knowing which form it took.
/// </summary>
public interface IReExport
{
    public Literal? ModuleSpecifier { get; }
    public string? ModulePath { get; }
    public bool IsTypeOnly { get; }

    /// <summary>False for an <see cref="ExportList" /> that names no module, which exports local names instead.</summary>
    public bool IsReExport { get; }

    /// <summary>Written with <c>internal</c> rather than <c>export</c> - see <see cref="Resolving.ExportBinding.IsInternal" />.</summary>
    public bool IsInternal { get; }
}
