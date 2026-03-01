using System.Numerics;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class EditorCameraTests
{
    [TestMethod]
    public void GetViewMatrix_DefaultPosition_IsValid()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        var view = cam.GetViewMatrix();

        // Should be invertible (non-degenerate)
        Assert.IsTrue(Matrix4x4.Invert(view, out _));
    }

    [TestMethod]
    public void GetProjectionMatrix_ReturnsValidPerspective()
    {
        var cam = new EditorCamera();
        var proj = cam.GetProjectionMatrix(16f / 9f);
        Assert.IsTrue(Matrix4x4.Invert(proj, out _));
    }

    [TestMethod]
    public void Rotate_ChangesForwardDirection()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        var before = cam.Forward;
        cam.Rotate(100f, 0f);
        var after = cam.Forward;

        Assert.AreNotEqual(before.X, after.X, 0.001f);
    }

    [TestMethod]
    public void Pan_MovesPosition()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        var before = cam.Position;
        cam.Pan(10f, 0f);
        var after = cam.Position;

        float dist = Vector3.Distance(before, after);
        Assert.IsGreaterThan(0.1f, dist, $"Camera should have moved, but distance was {dist}");
    }

    [TestMethod]
    public void Zoom_MovesAlongForward()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        var before = cam.Position;
        cam.Zoom(-10f); // negative dy = move forward
        var after = cam.Position;

        float dist = Vector3.Distance(before, after);
        Assert.IsGreaterThan(1f, dist);
    }

    [TestMethod]
    public void ZoomWheel_MovesForward()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        var before = cam.Position;
        cam.ZoomWheel(1f); // positive = forward
        var after = cam.Position;

        // Should move in the +X direction (forward)
        Assert.IsGreaterThan(before.X, after.X, "Scroll up should zoom in (move forward)");
    }

    [TestMethod]
    public void Reset_RestoresDefaultPosition()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        cam.Rotate(50, 30);
        cam.Pan(1, 2);
        cam.Reset();

        Assert.AreEqual(-2000f, cam.Position.X, 0.01f);
        Assert.AreEqual(0.314f, cam.Position.Y, 0.01f);
    }

    [TestMethod]
    public void LookAt_PointsCameraAtTarget()
    {
        var cam = new EditorCamera { ConstrainToDome = false };
        cam.LookAt(Vector3.Zero, 500f);

        // Camera should be pointing roughly toward origin
        var toTarget = Vector3.Normalize(-cam.Position);
        float dot = Vector3.Dot(cam.Forward, toTarget);
        Assert.IsGreaterThan(0.9f, dot, $"Camera should face target, dot={dot}");
    }

    [TestMethod]
    public void ConstrainToDome_ClampsPosition()
    {
        var cam = new EditorCamera
        {
            ConstrainToDome = true,
            DomeRadius = 100f
        };

        cam.Position = new Vector3(1000, 0, 0);
        cam.Zoom(-0.001f); // small move triggers clamp

        float r = cam.Position.Length();
        Assert.IsLessThanOrEqualTo(100f * 0.8f + 1f, r, $"Position should be clamped, but r={r}");
    }

    [TestMethod]
    public void ConstrainToDome_ClampsZAboveZero()
    {
        var cam = new EditorCamera
        {
            ConstrainToDome = true,
            DomeRadius = 10240f
        };

        cam.Position = new Vector3(0, 0, -100);
        cam.Pan(0, 0.001f); // triggers clamp

        Assert.IsGreaterThanOrEqualTo(0f, cam.Position.Z, $"Z should be >= 0, but was {cam.Position.Z}");
    }

    [TestMethod]
    public void Rotate_PreventFlipping_UpZStaysPositive()
    {
        var cam = new EditorCamera { ConstrainToDome = false };

        // Pitch down aggressively — should not flip past vertical
        for (int i = 0; i < 200; i++)
            cam.Rotate(0, 50f);

        Assert.IsGreaterThan(0f, cam.Up.Z, $"Up.Z should remain positive, but was {cam.Up.Z}");
    }
}
