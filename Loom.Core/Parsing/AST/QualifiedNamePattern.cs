namespace Loom.Core.Parsing.AST;

/// <summary>
///     A pattern that names one specific value by reference - <c>Direction.North</c> - rather than by
///     writing the value out as a literal. Wraps a real <see cref="QualifiedName" /> rather than a bare
///     pair of tokens so the resolver and type checker's existing reference-resolution and member-access
///     logic answers what it refers to and what it's worth; the pattern machinery only has to ask
///     whether the answer was a compile-time constant.
/// </summary>
public sealed class QualifiedNamePattern(QualifiedName name)
    : Pattern([], [name])
{
    public QualifiedName Name { get; } = name;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitQualifiedNamePattern(this);
}
