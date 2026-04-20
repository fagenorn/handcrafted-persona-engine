using PersonaEngine.Lib.Live2D.Framework.Model;

namespace PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;

public unsafe class CubismClippingContext_OpenGLES2(
    CubismClippingManager manager,
    CubismModel model,
    int* clippingDrawableIndices,
    int clipCount
) : CubismClippingContext(manager, clippingDrawableIndices, clipCount) { }
