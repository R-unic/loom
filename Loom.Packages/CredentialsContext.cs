using Tomlyn.Serialization;

namespace Loom.Packages;

[TomlSerializable(typeof(CredentialsDocument))]
internal sealed partial class CredentialsContext : TomlSerializerContext;
