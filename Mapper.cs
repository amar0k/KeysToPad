namespace KeysToPad;

/// <summary>One report of an Xbox 360 controller.</summary>
public struct PadState : IEquatable<PadState>
{
    public ushort Buttons;
    public byte LeftTrigger, RightTrigger;
    public short LX, LY, RX, RY;

    public readonly bool Equals(PadState o) =>
        Buttons == o.Buttons && LeftTrigger == o.LeftTrigger && RightTrigger == o.RightTrigger && LX == o.LX && LY == o.LY && RX == o.RX && RY == o.RY;
    public override readonly bool Equals(object? o) => o is PadState s && Equals(s);
    public override readonly int GetHashCode() => HashCode.Combine(Buttons, LeftTrigger, RightTrigger, LX, LY, RX, RY);
}

/// <summary>The controller's button bits (the same numbers games read).</summary>
public static class PadButtons
{
    public const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
        Start = 0x0010, Back = 0x0020, LeftStickClick = 0x0040, RightStickClick = 0x0080,
        LB = 0x0100, RB = 0x0200, Guide = 0x0400, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    public static ushort Bit(Target target) => target switch
    {
        Target.A => A, Target.B => B, Target.X => X, Target.Y => Y, Target.LB => LB, Target.RB => RB,
        Target.Back => Back, Target.Start => Start, Target.Guide => Guide,
        Target.LeftStickClick => LeftStickClick, Target.RightStickClick => RightStickClick,
        Target.DpadUp => DpadUp, Target.DpadDown => DpadDown, Target.DpadLeft => DpadLeft, Target.DpadRight => DpadRight,
        _ => 0,
    };
}

/// <summary>
/// Turns what is held on the keyboard and mouse, and how the mouse is moving, into controller reports.
/// No Windows calls in here, so it can be tested by itself.
/// </summary>
public sealed class Mapper
{
    /// <summary>Mouse counts per second that mean a fully pushed stick at sensitivity 1.</summary>
    public const double FullRate = 3000.0;

    /// <summary>Less mouse movement than this (in counts) left over is dropped, so the stick comes fully to rest.</summary>
    const double Leftover = 0.05;

    static readonly int Targets = Enum.GetValues<Target>().Length;
    static readonly Target[] ButtonTargets = Enum.GetValues<Target>().Where(t => PadButtons.Bit(t) != 0).ToArray();

    readonly object gate = new();
    readonly int[] held = new int[Targets];
    readonly double[] pulse = new double[Targets];
    Profile profile;
    double pendingX, pendingY;

    public Mapper(Profile profile) => this.profile = profile;

    public void SetProfile(Profile next)
    {
        lock (gate) { profile = next; ResetLocked(); }
    }

    /// <summary>Lets go of everything (used when controller mode is switched).</summary>
    public void Reset()
    {
        lock (gate) ResetLocked();
    }

    void ResetLocked()
    {
        Array.Clear(held);
        Array.Clear(pulse);
        pendingX = pendingY = 0;
    }

    public void Press(Target target, bool down)
    {
        if (target == Target.None) return;
        lock (gate) held[(int)target] = Math.Max(0, held[(int)target] + (down ? 1 : -1));
    }

    /// <summary>A short press, for inputs with no "release" such as a wheel notch.</summary>
    public void Pulse(Target target)
    {
        if (target == Target.None) return;
        lock (gate) pulse[(int)target] = profile.WheelPulseSeconds;
    }

    public void MouseMove(int dx, int dy)
    {
        lock (gate)
        {
            pendingX += dx;
            pendingY += dy * profile.VerticalRatio;
        }
    }

    /// <summary>The controller report for the next <paramref name="dt"/> seconds.</summary>
    public PadState Tick(double dt)
    {
        lock (gate)
        {
            var p = profile;
            var state = new PadState();
            foreach (var target in ButtonTargets)
                if (On(target)) state.Buttons |= PadButtons.Bit(target);
            if (On(Target.LT)) state.LeftTrigger = 255;
            if (On(Target.RT)) state.RightTrigger = 255;

            double lx = Axis(Target.LeftStickRight, Target.LeftStickLeft), ly = Axis(Target.LeftStickUp, Target.LeftStickDown);
            double rx = Axis(Target.RightStickRight, Target.RightStickLeft), ry = Axis(Target.RightStickUp, Target.RightStickDown);
            Circle(ref lx, ref ly);
            Circle(ref rx, ref ry);
            if (On(Target.Walk)) { lx *= p.WalkScale; ly *= p.WalkScale; }

            var (mx, my) = MouseStick(p, dt);
            if (p.MouseStick == StickSide.Right) { rx += mx; ry += my; Circle(ref rx, ref ry); }
            else if (p.MouseStick == StickSide.Left) { lx += mx; ly += my; Circle(ref lx, ref ly); }

            state.LX = ToShort(lx); state.LY = ToShort(ly);
            state.RX = ToShort(rx); state.RY = ToShort(ry);

            for (int i = 0; i < pulse.Length; i++)
                if (pulse[i] > 0) pulse[i] -= dt;
            return state;
        }
    }

    // A stick reports speed and a mouse reports distance. The mouse's movement is kept as distance
    // still to be "played"; each tick the stick is pushed in proportion to what is waiting and that
    // much is taken off. Below the stick's top speed this follows the hand with a little smoothing;
    // above it the stick stays fully pushed until the kept distance (limited by carryOverMs) is used up.
    (double x, double y) MouseStick(Profile p, double dt)
    {
        if (p.MouseStick == StickSide.None) { pendingX = pendingY = 0; return (0, 0); }
        double length = Math.Sqrt(pendingX * pendingX + pendingY * pendingY);
        if (length < Leftover) { pendingX = pendingY = 0; return (0, 0); }

        double full = FullRate / p.Sensitivity;                    // counts per second at a fully pushed stick
        double smoothing = Math.Max(p.SmoothingMs / 1000.0, dt);
        double most = full * (smoothing + p.CarryOverMs / 1000.0);  // the most distance that may wait
        if (length > most) { pendingX *= most / length; pendingY *= most / length; length = most; }

        double push = Math.Min(1.0, length / (full * smoothing));  // 0..1, before the game's dead zone and curve
        double ux = pendingX / length, uy = pendingY / length;
        double used = Math.Min(length, push * full * dt);
        pendingX -= ux * used;
        pendingY -= uy * used;

        double shown = p.DeadZone + (1 - p.DeadZone) * Math.Pow(push, p.Curve);
        // Mouse "down" is positive, stick "up" is positive.
        return (ux * shown, (p.InvertY ? uy : -uy) * shown);
    }

    bool On(Target target) => held[(int)target] > 0 || pulse[(int)target] > 0;

    double Axis(Target positive, Target negative) => (On(positive) ? 1 : 0) - (On(negative) ? 1 : 0);

    /// <summary>Keeps a stick inside its circle (so a diagonal is not pushed further than a straight line).</summary>
    static void Circle(ref double x, ref double y)
    {
        double length = Math.Sqrt(x * x + y * y);
        if (length > 1) { x /= length; y /= length; }
    }

    static short ToShort(double value) => (short)Math.Round(Math.Clamp(value, -1, 1) * 32767);
}
