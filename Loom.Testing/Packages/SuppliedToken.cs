using Loom.Packages;

namespace Loom.Testing.Packages;

/// <summary>
///     What <c>LOOM_TOKEN</c> holds for the length of a test, restored on the way out.
/// </summary>
/// <remarks>
///     Every test touching credentials sets it, including the ones expecting none: the variable is the machine's,
///     and a developer who has signed in would otherwise have a token quietly supplied to a case asserting that
///     there is nothing to send.
/// </remarks>
internal sealed class SuppliedToken : IDisposable
{
    private readonly string? _restore;

    public SuppliedToken(string? value)
    {
        _restore = Environment.GetEnvironmentVariable(RegistryCredentials.EnvironmentVariable);
        Environment.SetEnvironmentVariable(RegistryCredentials.EnvironmentVariable, value);
    }

    /// <summary>Nothing supplied by the environment, whatever the machine running the test has set.</summary>
    public static SuppliedToken None => new(null);

    public void Dispose() => Environment.SetEnvironmentVariable(RegistryCredentials.EnvironmentVariable, _restore);
}
