using System.Numerics;

namespace GiantsEdit.Core.Rendering;

/// <summary>
/// Free-flight camera matching the original Delphi editor's FlyAround controls.
/// Left drag = rotate, Right drag = pan, Both drag = zoom, Scroll = zoom.
/// Pure math — no rendering dependency.
/// </summary>
public class EditorCamera
{
    private Vector3 _eye = new(-2000f, 0.314f, 200f);
    private Vector3 _view = Vector3.UnitX;      // forward direction
    private Vector3 _up = Vector3.UnitZ;
    private Vector3 _right = Vector3.UnitY;

    public float FieldOfView { get; set; } = 65f;
    public float NearPlane { get; set; } = 40f;
    public float FarPlane { get; set; } = 40960f;

    /// <summary>Divisor for mouse-pixel-to-radians conversion during rotation. Default 100 matches original.</summary>
    public float RotateScale { get; set; } = 100f;

    /// <summary>Multiplier for mouse-pixel-to-world-unit conversion during pan/zoom drag. Default 30 matches original.</summary>
    public float PanSpeed { get; set; } = 30f;

    /// <summary>Multiplier for scroll wheel delta to world units.</summary>
    public float WheelSpeed { get; set; } = 150f;

    public float DomeRadius { get; set; } = 10240f;
    public bool ConstrainToDome { get; set; } = true;

    public Vector3 Position
    {
        get => _eye;
        set => _eye = value;
    }

    public Vector3 Forward => _view;
    public Vector3 Right => _right;
    public Vector3 Up => _up;

    /// <summary>
    /// Rotates the camera by mouse pixel delta (left-button drag).
    /// Matches original FlyAround movestate==1: yaw around Z, pitch in (view,up) plane.
    /// </summary>
    public void Rotate(float pixelDx, float pixelDy)
    {
        float yaw = -pixelDx / RotateScale;
        float pitch = pixelDy / RotateScale;

        // Yaw: rotate all three axes around world Z
        _view = RotateAroundZ(_view, yaw);
        _right = RotateAroundZ(_right, yaw);
        _up = RotateAroundZ(_up, yaw);

        // Pitch: rotate view and up in the (view, up) plane
        float cosP = MathF.Cos(pitch);
        float sinP = MathF.Sin(pitch);
        var newView = _view * cosP - _up * sinP;
        var newUp = _view * sinP + _up * cosP;

        // Prevent flipping: up must still point "upward" (Z > 0)
        if (newUp.Z > 0)
        {
            _view = newView;
            _up = newUp;
        }
    }

    /// <summary>
    /// Pans the camera by mouse pixel delta (right-button drag).
    /// Matches original FlyAround movestate==2.
    /// </summary>
    public void Pan(float pixelDx, float pixelDy)
    {
        _eye -= _right * (pixelDx * PanSpeed);
        _eye -= _up * (pixelDy * PanSpeed);
        ClampToDome();
    }

    /// <summary>
    /// Zooms the camera by mouse pixel delta (both-button drag, Y axis).
    /// Matches original FlyAround movestate==3.
    /// </summary>
    public void Zoom(float pixelDy)
    {
        _eye -= _view * (pixelDy * PanSpeed);
        ClampToDome();
    }

    /// <summary>
    /// Zooms the camera by scroll wheel delta. Positive = forward.
    /// </summary>
    public void ZoomWheel(float wheelDelta)
    {
        _eye += _view * (wheelDelta * WheelSpeed);
        ClampToDome();
    }

    /// <summary>
    /// Returns the view matrix for this camera.
    /// Uses world-up (0,0,1) matching the original gluLookAt call.
    /// </summary>
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(_eye, _eye + _view, Vector3.UnitZ);
    }

    /// <summary>
    /// Returns the projection matrix for the given aspect ratio.
    /// </summary>
    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfView * MathF.PI / 180f,
            aspectRatio,
            NearPlane,
            FarPlane);
    }

    /// <summary>
    /// Resets camera to default position looking at the origin.
    /// </summary>
    public void Reset()
    {
        _eye = new Vector3(-2000f, 0.314f, 200f);
        _view = Vector3.UnitX;
        _right = Vector3.UnitY;
        _up = Vector3.UnitZ;
    }

    /// <summary>
    /// Moves the camera to look at a specific world position.
    /// </summary>
    public void LookAt(Vector3 target, float distance = 500f)
    {
        _view = Vector3.Normalize(target - _eye);

        // Recompute right/up from view direction
        _right = Vector3.Normalize(Vector3.Cross(_view, Vector3.UnitZ));
        _up = Vector3.Normalize(Vector3.Cross(_right, _view));

        _eye = target - _view * distance;
        ClampToDome();
    }

    private static Vector3 RotateAroundZ(Vector3 v, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new Vector3(
            v.X * cos - v.Y * sin,
            v.Y * cos + v.X * sin,
            v.Z);
    }

    private void ClampToDome()
    {
        if (!ConstrainToDome) return;

        if (_eye.Z < 0)
            _eye = new Vector3(_eye.X, _eye.Y, 0);

        float r2 = _eye.X * _eye.X + _eye.Y * _eye.Y + _eye.Z * _eye.Z;
        float maxR = DomeRadius * 0.8f;
        if (r2 > maxR * maxR)
        {
            float scale = maxR / MathF.Sqrt(r2);
            _eye *= scale;
        }
    }
}
