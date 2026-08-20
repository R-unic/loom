using Tomlyn.Serialization;

namespace Loom.Config;

[TomlSerializable(typeof(LockFileDocument))]
internal sealed partial class LockFileContext : TomlSerializerContext;
