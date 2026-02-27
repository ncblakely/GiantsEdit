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
    public Vector3 Rotation; // Euler angles (radians)
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
