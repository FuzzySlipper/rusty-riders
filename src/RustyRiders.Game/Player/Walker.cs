using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Input;

namespace RustyRiders.Game.Player;

/// <summary>A first-person walker: Engine FpsInput and Look for controls, Engine character steps for movement.</summary>
internal sealed class Walker : IDisposable
{
    private const double NearPlane = .05;
    private const float EyeBelowTop = .15f;

    private readonly IEngineContext engine;
    private readonly WalkerTuning tuning;
    private readonly CharacterControllerConfig config;
    private readonly CharacterControllerConfig sprintConfig;
    private readonly Camera camera;
    private IWalkScene scene;
    private ulong commandSequence;
    private bool cut = true;

    internal Walker(IEngineContext engine, WalkerTuning tuning, IWalkScene scene)
    {
        this.engine = engine;
        this.tuning = tuning;
        this.scene = scene;
        config = Configure(engine.Spatial.DefaultCharacterControllerConfig(), tuning.Speed);
        sprintConfig = Configure(config, tuning.SprintSpeed);
        engine.Spatial.ValidateCharacterControllerConfig(config);
        engine.Spatial.ValidateCharacterControllerConfig(sprintConfig);
        Input = new FpsInput(FpsInputConfig.Standard with
        {
            PointerLookConfig = FpsInputConfig.Standard.PointerLookConfig with
            { HorizontalRadiansPerUnit = tuning.PointerRadiansPerUnit, VerticalRadiansPerUnit = tuning.PointerRadiansPerUnit }
        });
        Reset();
        camera = engine.CameraView.CreateCamera(Descriptor());
        engine.CameraView.SetActiveCamera(camera);
    }

    internal FpsInput Input { get; }
    /// <summary>Free flight: converted meshes do not collide, so walking cannot climb them.</summary>
    internal bool Flying { get; private set; }
    internal LookState LookState { get; private set; }
    /// <summary>Walking off the ground: in a jump or a fall.</summary>
    internal bool Airborne => !Flying && !Motion.Grounded;
    internal Vector3 Position { get; private set; }
    internal CharacterMotion Motion { get; private set; }
    internal float Height => Motion.Stance == CharacterStance.Crouched ? tuning.CrouchedHeight : tuning.Height;
    internal Vector3 Feet => Position - Vector3.UnitY * (Height / 2);
    private Vector3 Eye => Position + Vector3.UnitY * (Height / 2 - EyeBelowTop);
    internal Vector3 EyePosition => Eye;
    /// <summary>Where the walker looks, as a unit vector (pitch included): the aim of its actions.</summary>
    internal Vector3 Forward => Look.IntegrateClamped(new LookRequest(LookState, Vector2.Zero, Input.Config.PointerLookConfig)).Forward;

    /// <param name="hostSeconds">Unscaled host seconds since the last update, so controller look keeps its speed
    /// while gameplay time holds or slows the world.</param>
    internal FpsInputFrame ReadInput(ReadOnlySpan<ProductInputEvent> events, float hostSeconds)
    {
        FpsInputFrame frame = Input.Consume(events, hostSeconds);
        LookState = Input.IntegrateLook(LookState, frame).After;
        return frame;
    }

    internal void ToggleFlight()
    {
        Flying = !Flying;
        Motion = default; // leaving flight starts a fresh fall from where the walker hovers
    }

    /// <param name="movementScale">How much of the movement input moves the body (stuns and slows).</param>
    /// <param name="externalVelocity">A velocity driving the body regardless of input (a knockback).</param>
    internal void Step(FpsInputFrame input, bool jumpPressed, float delta, float movementScale, Vector3 externalVelocity)
    {
        if (Flying)
        {
            Fly(input, delta);
            return;
        }
        CharacterStepReceipt receipt = engine.Spatial.ProposeCharacterStep(new CharacterStepRequest(scene.Session, Position, Motion,
            default, ReadOnlyMemory<CharacterObstacle>.Empty, ReadOnlyMemory<CharacterMeshInstance>.Empty,
            input.SprintHeld && !input.CrouchHeld ? sprintConfig : config,
            new CharacterControllerCommand(input.Movement * movementScale, LookState.YawRadians, jumpPressed && movementScale > 0,
                input.JumpHeld, input.CrouchHeld, externalVelocity, Vector3.Zero, delta, ++commandSequence)));
        Position = receipt.Transform.Translation;
        Motion = receipt.Motion;
    }

    private void Fly(FpsInputFrame input, float delta)
    {
        Vector3 forward = Look.IntegrateClamped(new LookRequest(LookState, Vector2.Zero, Input.Config.PointerLookConfig)).Forward;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        float vertical = (input.JumpHeld ? 1 : 0) - (input.CrouchHeld ? 1 : 0);
        float speed = input.SprintHeld ? tuning.FlySprintSpeed : tuning.FlySpeed;
        Vector3 position = Position + (forward * input.Movement.Y + right * input.Movement.X + Vector3.UnitY * vertical) * speed * delta;
        Position = position with { Y = Math.Max(position.Y, Height / 2) };
    }

    /// <summary>Moves the walker into another scene's collision, at that scene's spawn.</summary>
    internal void Enter(IWalkScene next)
    {
        scene = next;
        Reset();
    }

    /// <summary>Stands the walker at <paramref name="feet"/> facing a yaw (developer overrides).</summary>
    internal void Place(Vector3 feet, float yawDegrees)
    {
        Position = feet + Vector3.UnitY * (Height / 2);
        Motion = default;
        LookState = new LookState(yawDegrees * MathF.PI / 180, 0);
        cut = true;
    }

    /// <summary>The walker yaw that faces from one point to another (positive yaw turns right; 0 faces -Z).</summary>
    internal static float YawDegreesToward(Vector3 from, Vector3 to) => MathF.Atan2(to.X - from.X, -(to.Z - from.Z)) * 180 / MathF.PI;

    internal void Reset()
    {
        Position = scene.SpawnFeet + Vector3.UnitY * (tuning.Height / 2);
        Motion = default;
        Flying = false;
        LookState = new LookState(scene.SpawnYawDegrees * MathF.PI / 180, 0);
        Input.Physical.Clear();
        cut = true;
    }

    internal void Publish(double sampleTime)
    {
        engine.CameraView.UpdateCameraSample(new CameraSampleRequest(camera, Descriptor(), sampleTime, 1d / 60,
            CameraInterpolation.Position, cut ? (byte)1 : (byte)0));
        cut = false;
    }

    public void Dispose()
    {
        engine.CameraView.ClearActiveCamera(new ClearActiveCameraRequest(0));
        camera.Dispose();
    }

    private CameraDescriptor Descriptor() => new(new CameraPose(Eye, LookState.PitchRadians * 180 / Math.PI, LookState.YawRadians * 180 / Math.PI),
        CameraBasisMode.Derived, default,
        new CameraProjection(CameraProjectionKind.Perspective, tuning.FieldOfViewDegrees, 0, NearPlane, tuning.FarPlane),
        new CameraViewport(0, 0, 1, 1));

    private CharacterControllerConfig Configure(CharacterControllerConfig baseline, float speed) => baseline with
    {
        Shape = baseline.Shape with { StandingHeight = tuning.Height, CrouchedHeight = tuning.CrouchedHeight, Radius = tuning.Radius },
        Ground = baseline.Ground with { ForwardSpeed = speed, BackwardSpeed = speed, StrafeSpeed = speed },
        Air = baseline.Air with { MaximumSpeed = speed, WishSpeedCap = speed },
        Vertical = baseline.Vertical with { Gravity = tuning.Gravity, JumpSpeed = tuning.JumpSpeed },
        Surface = baseline.Surface with { MaximumStepHeight = tuning.MaximumStepHeight, MaximumSlopeRadians = tuning.MaximumSlopeDegrees * MathF.PI / 180 }
    };
}
