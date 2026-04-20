using System.Text.Json.Serialization;
using PersonaEngine.Lib.Live2D.Framework.Model;
using PersonaEngine.Lib.Live2D.Framework.Motion;
using PersonaEngine.Lib.Live2D.Framework.Physics;

namespace PersonaEngine.Lib.Live2D.Framework;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ModelSettingObj))]
public partial class ModelSettingObjContext : JsonSerializerContext { }

[JsonSerializable(typeof(CubismMotionObj))]
public partial class CubismMotionObjContext : JsonSerializerContext { }

[JsonSerializable(typeof(CubismModelUserDataObj))]
public partial class CubismModelUserDataObjContext : JsonSerializerContext { }

[JsonSerializable(typeof(CubismPhysicsObj))]
public partial class CubismPhysicsObjContext : JsonSerializerContext { }
