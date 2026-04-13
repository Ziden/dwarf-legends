using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public sealed class WorldCamera3DController
{
    private const float DefaultFocusTile = 24f;
    private const float DefaultCameraSize = 24f;
    private const float MinCameraSize = 10f;
    private const float MaxCameraSize = 64f;
    private const float BasePanSpeedTiles = 18f;
    private const float DefaultYawRadians = -Mathf.Pi * 0.25f;
    private const float MinOrbitDistance = 12f;
    private const float MaxOrbitDistance = 52f;
    private const float RotationSensitivityRadians = 0.01f;
    private static readonly float LockedPitchRadians = Mathf.DegToRad(40f);

    private Camera3D? _camera;
    private Vector2 _focusTile = new(DefaultFocusTile, DefaultFocusTile);
    private float _yawRadians = DefaultYawRadians;
    private bool _cameraMovedSinceLastUpdate;
    private bool _isRotating;

    public bool CameraMoved => _cameraMovedSinceLastUpdate;
    public Vector2 FocusTile => _focusTile;
    public float YawRadians => _yawRadians;
    public bool IsRotating => _isRotating;

    public void Initialize(Camera3D? camera)
    {
        _camera = camera;
        if (_camera is null)
            return;

        _camera.Current = true;
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Near = 0.1f;
        _camera.Far = 512f;
        _camera.Size = DefaultCameraSize;
        _cameraMovedSinceLastUpdate = true;
    }

    public void SetView(Vector2 focusTile, float cameraSize = DefaultCameraSize)
    {
        _focusTile = focusTile;
        if (_camera is not null)
            _camera.Size = Mathf.Clamp(cameraSize, MinCameraSize, MaxCameraSize);
        _cameraMovedSinceLastUpdate = true;
    }

    public void HandleCameraMovement(double delta)
    {
        _cameraMovedSinceLastUpdate = false;
        if (_camera is null)
            return;

        MoveFocus(ResolveMovementInput(), delta);
    }

    public void ApplyZoom(float factor)
    {
        if (_camera is null)
            return;

        var next = Mathf.Clamp(_camera.Size * factor, MinCameraSize, MaxCameraSize);
        if (Mathf.IsEqualApprox(_camera.Size, next))
            return;

        _camera.Size = next;
        _cameraMovedSinceLastUpdate = true;
    }

    public void JumpToTile(Vec3i pos)
    {
        _focusTile = new Vector2(pos.X, pos.Y);
        _cameraMovedSinceLastUpdate = true;
    }

    public void MoveFocus(Vector2 moveInput, double delta)
    {
        if (_camera is null || moveInput == Vector2.Zero)
            return;

        var forward = ResolvePlanarForward();
        var right = new Vector2(-forward.Y, forward.X);
        var worldDirection = (right * moveInput.X) + (forward * moveInput.Y);
        if (worldDirection == Vector2.Zero)
            return;

        _focusTile += worldDirection.Normalized() * (float)delta * ResolvePanSpeedTilesPerSecond();
        _cameraMovedSinceLastUpdate = true;
    }

    public void RotateYaw(float deltaRadians)
    {
        if (Mathf.IsZeroApprox(deltaRadians))
            return;

        _yawRadians = Mathf.Wrap(_yawRadians + deltaRadians, -Mathf.Pi, Mathf.Pi);
        _cameraMovedSinceLastUpdate = true;
    }

    public bool HandlePointerInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, ShiftPressed: true }:
                _isRotating = true;
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _isRotating:
                _isRotating = false;
                return true;

            case InputEventMouseMotion motion when _isRotating:
                if (!motion.ShiftPressed || (motion.ButtonMask & MouseButtonMask.Left) == 0)
                {
                    _isRotating = false;
                    return true;
                }

                RotateYaw(-motion.Relative.X * RotationSensitivityRadians);
                return true;

            default:
                return false;
        }
    }

    public void SyncTransform(int currentZ)
    {
        if (_camera is null)
            return;

        var focusPoint = new Vector3(
            _focusTile.X,
            currentZ * WorldRender3D.VerticalSliceSpacing,
            _focusTile.Y);
        var distance = Mathf.Lerp(MinOrbitDistance, MaxOrbitDistance, ResolveCameraSizeT(_camera.Size));
        var height = distance * Mathf.Tan(LockedPitchRadians);
        var orbitDirection = new Vector3(Mathf.Sin(_yawRadians), 0f, Mathf.Cos(_yawRadians));

        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Position = focusPoint + (orbitDirection * distance) + (Vector3.Up * height);
        _camera.LookAt(focusPoint, Vector3.Up);
    }

    public Rect2I CalculateVisibleTileBounds(Viewport viewport, WorldMap? map)
    {
        if (_camera is null || map is null || map.Width <= 0 || map.Height <= 0)
            return new Rect2I();

        var viewportRect = viewport.GetVisibleRect();
        var aspect = viewportRect.Size.Y <= 0f
            ? 1f
            : viewportRect.Size.X / viewportRect.Size.Y;
        var halfSpanTiles = Mathf.Max(8f, (_camera.Size * Mathf.Max(aspect * 1.05f, 1.10f)) + 4f);

        var minX = Mathf.Clamp(Mathf.FloorToInt(_focusTile.X - halfSpanTiles), 0, map.Width - 1);
        var minY = Mathf.Clamp(Mathf.FloorToInt(_focusTile.Y - halfSpanTiles), 0, map.Height - 1);
        var maxX = Mathf.Clamp(Mathf.CeilToInt(_focusTile.X + halfSpanTiles), 0, map.Width - 1);
        var maxY = Mathf.Clamp(Mathf.CeilToInt(_focusTile.Y + halfSpanTiles), 0, map.Height - 1);

        return new Rect2I(minX, minY, Mathf.Max(1, maxX - minX + 1), Mathf.Max(1, maxY - minY + 1));
    }

    private float ResolvePanSpeedTilesPerSecond()
    {
        if (_camera is null)
            return BasePanSpeedTiles;

        return BasePanSpeedTiles * Mathf.Clamp(_camera.Size / 24f, 0.75f, 3.5f);
    }

    private static Vector2 ResolveMovementInput()
    {
        var moveInput = Vector2.Zero;

        if (Godot.Input.IsActionPressed("ui_left") || Godot.Input.IsPhysicalKeyPressed(Key.A))
            moveInput.X -= 1f;
        if (Godot.Input.IsActionPressed("ui_right") || Godot.Input.IsPhysicalKeyPressed(Key.D))
            moveInput.X += 1f;
        if (Godot.Input.IsActionPressed("ui_up") || Godot.Input.IsPhysicalKeyPressed(Key.W))
            moveInput.Y += 1f;
        if (Godot.Input.IsActionPressed("ui_down") || Godot.Input.IsPhysicalKeyPressed(Key.S))
            moveInput.Y -= 1f;

        return moveInput;
    }

    private Vector2 ResolvePlanarForward()
        => new(-Mathf.Sin(_yawRadians), -Mathf.Cos(_yawRadians));

    private static float ResolveCameraSizeT(float size)
        => Mathf.Clamp((size - MinCameraSize) / (MaxCameraSize - MinCameraSize), 0f, 1f);
}
