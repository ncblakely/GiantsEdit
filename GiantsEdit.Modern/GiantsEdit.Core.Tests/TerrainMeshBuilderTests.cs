using GiantsEdit.Core.Formats;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class TerrainMeshBuilderTests
{
    [TestMethod]
    public void Build_FlatTerrain_GeneratesCorrectVertexCount()
    {
        var terrain = GtiFormat.CreateNew(4, 4);
        // Set all triangles to type 1 (both triangles per cell)
        for (int i = 0; i < terrain.Triangles.Length; i++)
            terrain.Triangles[i] = 1;

        var mesh = TerrainMeshBuilder.Build(terrain);

        Assert.AreEqual(16, mesh.VertexCount); // 4x4
        Assert.AreEqual(16 * 3, mesh.Positions.Length); // 3 floats per vertex
    }

    [TestMethod]
    public void Build_AllTriType1_Generates2TrianglesPerCell()
    {
        var terrain = GtiFormat.CreateNew(3, 3);
        for (int i = 0; i < terrain.Triangles.Length; i++)
            terrain.Triangles[i] = 1;

        var mesh = TerrainMeshBuilder.Build(terrain);

        // 2x2 inner cells, 2 triangles each, 3 indices each = 24
        Assert.AreEqual(24, mesh.IndexCount);
    }

    [TestMethod]
    public void Build_EmptyTriType_GeneratesNoIndices()
    {
        var terrain = GtiFormat.CreateNew(3, 3);
        // Explicitly set all triangles to type 0 (empty)
        Array.Fill(terrain.Triangles, (byte)0);

        var mesh = TerrainMeshBuilder.Build(terrain);

        Assert.AreEqual(0, mesh.IndexCount);
    }

    [TestMethod]
    public void Build_VertexPositions_IncludeOffsetAndStretch()
    {
        var terrain = GtiFormat.CreateNew(2, 2);
        terrain.Header.XOffset = 100f;
        terrain.Header.YOffset = 200f;
        terrain.Header.Stretch = 10f;
        terrain.Heights[0] = 50f;

        var mesh = TerrainMeshBuilder.Build(terrain);

        // First vertex: x=0*10+100=100, y=0*10+200=200, z=50
        Assert.AreEqual(100f, mesh.Positions[0]);
        Assert.AreEqual(200f, mesh.Positions[1]);
        Assert.AreEqual(50f, mesh.Positions[2]);
    }

    [TestMethod]
    public void Build_Colors_PackedAsRGBA()
    {
        var terrain = GtiFormat.CreateNew(2, 2);
        terrain.LightMap[0] = 0xFF; // R
        terrain.LightMap[1] = 0x80; // G
        terrain.LightMap[2] = 0x40; // B

        var mesh = TerrainMeshBuilder.Build(terrain);

        uint expected = 0xFF | (0x80u << 8) | (0x40u << 16) | (0xFFu << 24);
        Assert.AreEqual(expected, mesh.Colors[0]);
    }
}
