using Tomlyn.Serialization;

namespace Loom.Config;

[TomlSerializable(typeof(LoomConfig))]
internal sealed partial class LoomConfigContext : TomlSerializerContext;
