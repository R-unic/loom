using Loom.Config;
using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     Reads the realm a declaration's own <c>[server]</c>/<c>[client]</c> attribute narrows it to. Shared
///     by the two points such a declaration is ever reached from: an import crossing a module boundary
///     (<see cref="Resolving.Resolver" />) and a Roblox API member reached through a value
///     (<see cref="TypeChecking.TypeChecker" />) - both ask the same question of a <see cref="Symbol" />, so
///     both ask it here.
/// </summary>
public static class RealmAttributes
{
    /// <summary>
    ///     The realm <paramref name="symbol" />'s declaration narrows it to, or <see langword="null" /> when
    ///     it narrows to none. Read off the declaration rather than <see cref="Symbol.Attributes" />, which
    ///     only some symbol kinds carry - the attribute is written on the declaration whatever kind it turns
    ///     out to declare.
    /// </summary>
    public static Realm? Of(Symbol symbol) =>
        symbol.Declaration is not IWithAttributes { Attributes: { } attributes } ? null
        : attributes.AttributeList.Exists(attribute => attribute.Name == "server") ? Realm.Server
        : attributes.AttributeList.Exists(attribute => attribute.Name == "client") ? Realm.Client
        : null;
}
