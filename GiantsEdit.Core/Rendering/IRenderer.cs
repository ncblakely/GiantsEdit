using System.Numerics;
using GiantsEdit.Core.DataModel;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.Core.Rendering;

/// <summary>
/// Abstraction over the rendering backend (OpenGL, Vulkan, etc.).
/// All rendering calls go through this interface so the backend can be swapped.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Initialise GPU resources (shaders, buffers). Called once.</summary>
    void Init(int viewportWidth, int viewportHeight);

    /// <summary>Called when the viewport is resized.</summary>
    void Resize(int width, int height);

    /// <summary>Render one frame.</summary>
    void Render(RenderState state);

    /// <summary>Begin rendering to MSAA framebuffer if available.</summary>
    void BeginRender(uint targetFramebuffer);

    /// <summary>Resolve MSAA framebuffer to target if active.</summary>
    void EndRender();

    /// <summary>Upload terrain mesh data to the GPU.</summary>
    void UploadTerrain(TerrainRenderData terrain);

    /// <summary>Upload a GBS model for later instanced drawing.</summary>
    int UploadModel(ModelRenderData model, int modelId = -1);

    /// <summary>Upload map object shapes and type→shape mapping for editor object rendering.</summary>
    void UploadMapObjects(Formats.MapObjectReader mapObjects);

    /// <summary>Release all GPU resources.</summary>
    void Cleanup();
}

/// <summary>
/// Everything the renderer needs to draw a single frame.
/// </summary>
public class RenderState
{
    public required Matrix4x4 ViewMatrix { get; init; }
    public required Matrix4x4 ProjectionMatrix { get; init; }
    public required Vector3 CameraPosition { get; init; }

    /// <summary>Objects to draw this frame.</summary>
    public List<ObjectInstance> Objects { get; init; } = [];

    // Visual toggles
    public bool ShowTerrain { get; init; } = true;
    public bool ShowDome { get; init; } = true;
    public bool ShowSea { get; init; } = true;
    public bool ShowObjects { get; init; } = true;
    public bool ShowTerrainMesh { get; init; }
    public bool DrawRealObjects { get; init; }
    public bool ViewObjThruTerrain { get; init; }

    /// <summary>The source node of the currently selected object (null if none).</summary>
    public TreeNode? SelectedObjectNode { get; init; }

    // Fog
    public Vector3 FogColor { get; init; }
    public float FogNear { get; init; } = 100f;
    public float FogFar { get; init; } = 10000f;

    // Sea
    public Vector3 SeaColor { get; init; } = new(0, 0.2f, 0.4f);
    public float SeaLevel { get; init; }

    // Dome
    public float DomeRadius { get; init; } = 10240f;

    /// <summary>Spline line segments connecting waypoint objects.</summary>
    public List<SplineLine> SplineLines { get; init; } = [];
}

/// <summary>
/// A batch of line segments sharing the same color.
/// Vertices are pairs of endpoints: [p0, p1, p2, p3, ...] drawn as GL_LINES.
/// </summary>
public class SplineLine
{
    public required float[] Vertices { get; init; } // 3 floats per point (x,y,z)
    public int PointCount { get; init; }
    public Vector3 Color { get; init; } = Vector3.One;
}

/// <summary>
/// A positioned object in the scene.
/// </summary>
public struct ObjectInstance
{
    public int ModelId;
    public Vector3 Position;
    public float DirFacing;   // degrees
    public float TiltForward; // degrees
    public float TiltLeft;    // degrees
    public float Scale;
    public TreeNode? SourceNode;
}

/// <summary>
/// Terrain data ready for GPU upload.
/// </summary>
public class TerrainRenderData
{
    /// <summary>Vertex positions: 3 floats per vertex (x, y, z).</summary>
    public required float[] Positions { get; init; }
    /// <summary>Vertex colors: 4 bytes per vertex (R, G, B, A) packed as uint.</summary>
    public required uint[] Colors { get; init; }
    /// <summary>Triangle indices into the vertex array.</summary>
    public required uint[] Indices { get; init; }
    public int VertexCount { get; init; }
    public int IndexCount { get; init; }

    /// <summary>Terrain texture images and tiling values (null if no game data).</summary>
    public TerrainTextureInfo? Textures { get; set; }
}

/// <summary>
/// Texture information for terrain rendering: ground, slope, and wall textures with tiling.
/// </summary>
public class TerrainTextureInfo
{
    public Formats.TgaImage? GroundImage { get; set; }
    public float GroundWrap { get; set; } = 100f;
    public Formats.TgaImage? SlopeImage { get; set; }
    public float SlopeWrap { get; set; } = 100f;
    public Formats.TgaImage? WallImage { get; set; }
    public float WallWrap { get; set; } = 100f;
}

/// <summary>
/// Model data ready for GPU upload.
/// </summary>
public class ModelRenderData
{
    /// <summary>Interleaved vertex data: position(3) + normal(3) + uv(2) + color(3).</summary>
    public required float[] Vertices { get; init; }
    /// <summary>Triangle indices.</summary>
    public required uint[] Indices { get; init; }
    public int VertexCount { get; init; }
    public int IndexCount { get; init; }
    /// <summary>Stride in floats per vertex (11 = pos3 + normal3 + uv2 + color3).</summary>
    public int VertexStride { get; init; } = 11;

    public List<ModelPartData> Parts { get; init; } = [];

    /// <summary>Axis-aligned bounding box minimum corner.</summary>
    public Vector3 BoundsMin { get; set; }
    /// <summary>Axis-aligned bounding box maximum corner.</summary>
    public Vector3 BoundsMax { get; set; }
}

/// <summary>
/// A sub-part of a model with its own material/texture.
/// </summary>
public class ModelPartData
{
    public int IndexOffset { get; init; }
    public int IndexCount { get; init; }
    public string TextureName { get; init; } = string.Empty;
    public bool HasAlpha { get; init; }

    /// <summary>Loaded texture image (set by ModelManager before upload).</summary>
    public Formats.TgaImage? TextureImage { get; set; }
}
