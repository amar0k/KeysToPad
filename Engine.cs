using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeysToPad;

/// <summary>Where finished controller reports go (the virtual controller, or a recorder in tests).</summary>
public interface IPad
{
    void Submit(in PadState state);
}

/// <summary>
/// Watches the keyboard and mouse, and while controller mode is on keeps the mapped keys and the mouse
/// away from Windows and sends them to the controller instead.
/// Start() must be called on a thread that runs a Windows message loop (the tray's thread).
/// </summary>
internal sealed unsafe class Engine : IDisposable
{
    readonly IPad pad;
    readonly Mapper mapper;
    volatile Profile profile;
    volatile bool active;
    volatile bool stopping;

    // Only touched on the message-loop thread (where Windows calls the hooks).
    // A key or button release is kept from Windows only if its press was, so Windows never ends up
    // believing something is held that is not (or the other way round).
    readonly HashSet<int> downKeys = new();
    readonly Dictionary<int, Target> ownedKeys = new();
    readonly Target?[] ownedButtons = new Target?[5];

    Native.HookProc? keyboardProc, mouseProc;
    IntPtr keyboardHook, mouseHook;
    RawMouseWindow? rawWindow;
    Thread? ticker;

    public event Action<bool>? ActiveChanged;
    public bool Active => active;

    public Engine(IPad pad, Profile profile)
    {
        this.pad = pad;
        this.profile = profile;
        mapper = new Mapper(profile);
    }

    public void Start()
    {
        keyboardProc = KeyboardProc;
        mouseProc = MouseProc;
        IntPtr module = Native.GetModuleHandle(null);
        keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
        mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            throw new InvalidOperationException($"Windows refused the keyboard or mouse watch (error {Marshal.GetLastWin32Error()}).");
        rawWindow = new RawMouseWindow(this);
        ticker = new Thread(TickLoop) { IsBackground = true, Name = "KeysToPad reports", Priority = ThreadPriority.AboveNormal };
        ticker.Start();
        if (profile.StartInControllerMode) SetActive(true);
    }

    public void SetProfile(Profile next)
    {
        profile = next;
        mapper.SetProfile(next);
    }

    public void Toggle() => SetActive(!active);

    // The built-in checks drive the same rules Windows' hooks do, without Windows.
    internal bool TestKey(int key, bool down) => HandleKey(key, down);
    internal bool TestMouse(int message, uint data) => HandleMouse(message, data);
    internal PadState TestTick(double dt) => active ? mapper.Tick(dt) : default;

    public void SetActive(bool on)
    {
        if (active == on) return;
        active = on;
        mapper.Reset();
        ActiveChanged?.Invoke(on);
    }

    // ---- keyboard ----

    IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == 0)
        {
            try
            {
                var info = (Native.KBDLLHOOKSTRUCT*)lParam;
                int message = (int)wParam;
                bool down = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                if (HandleKey((int)info->vkCode, down)) return 1;
            }
            catch { /* never let an error reach Windows from inside a hook */ }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>True = keep this key event from Windows.</summary>
    bool HandleKey(int key, bool down)
    {
        if (!down)
        {
            downKeys.Remove(key);
            if (!ownedKeys.Remove(key, out var target)) return false;
            mapper.Press(target, false);
            return true;
        }

        bool repeat = !downKeys.Add(key);
        if (ownedKeys.ContainsKey(key)) return true;            // a held key repeating
        if (repeat) return false;                               // held since before we took over: Windows keeps it
        var p = profile;
        if (IsToggle(p.Toggle, key))
        {
            ownedKeys[key] = Target.None;
            Toggle();
            return true;
        }
        if (!active) return false;
        if (p.Keys.TryGetValue(key, out var to) || p.BlockAllKeys)
        {
            ownedKeys[key] = to;
            mapper.Press(to, true);
            return true;
        }
        return false;
    }

    bool IsToggle(Hotkey hotkey, int key) =>
        key == hotkey.Key
        && (!hotkey.Ctrl || Held(0xA2) || Held(0xA3))
        && (!hotkey.Alt || Held(0xA4) || Held(0xA5))
        && (!hotkey.Shift || Held(0xA0) || Held(0xA1))
        && (!hotkey.Win || Held(0x5B) || Held(0x5C));

    // Windows knows about keys we let through; we know about the ones we kept.
    bool Held(int key) => ownedKeys.ContainsKey(key) || (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    // ---- mouse ----

    IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == 0)
        {
            try
            {
                if (HandleMouse((int)wParam, ((Native.MSLLHOOKSTRUCT*)lParam)->mouseData)) return 1;
            }
            catch { }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>True = keep this mouse event from Windows.</summary>
    bool HandleMouse(int message, uint data)
    {
        var p = profile;
        switch (message)
        {
            case Native.WM_MOUSEMOVE:
                // The movement itself arrives through the raw mouse window; here the pointer is just held still.
                return active && p.MouseStick != StickSide.None;
            case Native.WM_LBUTTONDOWN: return ButtonDown(MouseInput.Left, p);
            case Native.WM_LBUTTONUP: return ButtonUp(MouseInput.Left);
            case Native.WM_RBUTTONDOWN: return ButtonDown(MouseInput.Right, p);
            case Native.WM_RBUTTONUP: return ButtonUp(MouseInput.Right);
            case Native.WM_MBUTTONDOWN: return ButtonDown(MouseInput.Middle, p);
            case Native.WM_MBUTTONUP: return ButtonUp(MouseInput.Middle);
            case Native.WM_XBUTTONDOWN: return ButtonDown((data >> 16) == 1 ? MouseInput.Back : MouseInput.Forward, p);
            case Native.WM_XBUTTONUP: return ButtonUp((data >> 16) == 1 ? MouseInput.Back : MouseInput.Forward);
            case Native.WM_MOUSEWHEEL:
            {
                if (!active) return false;
                var to = p.MouseButtons[(int)((short)(data >> 16) > 0 ? MouseInput.WheelUp : MouseInput.WheelDown)];
                if (to == Target.None) return false;
                mapper.Pulse(to);
                return true;
            }
            default:
                return false;
        }
    }

    bool ButtonDown(MouseInput button, Profile p)
    {
        if (!active) return false;
        var to = p.MouseButtons[(int)button];
        if (to == Target.None) return false;
        ownedButtons[(int)button] = to;
        mapper.Press(to, true);
        return true;
    }

    bool ButtonUp(MouseInput button)
    {
        if (ownedButtons[(int)button] is not { } to) return false;
        ownedButtons[(int)button] = null;
        mapper.Press(to, false);
        return true;
    }

    void RawMouse(int dx, int dy)
    {
        if (active && profile.MouseStick != StickSide.None) mapper.MouseMove(dx, dy);
    }

    // ---- reports ----

    void TickLoop()
    {
        Native.timeBeginPeriod(1);
        try
        {
            var clock = Stopwatch.StartNew();
            double last = 0;
            var sent = new PadState();
            pad.Submit(sent);
            while (!stopping)
            {
                Thread.Sleep(1);
                double now = clock.Elapsed.TotalSeconds;
                double dt = Math.Clamp(now - last, 0.0002, 0.05);
                last = now;
                var state = active ? mapper.Tick(dt) : default;
                if (state.Equals(sent)) continue;
                try { pad.Submit(state); sent = state; }
                catch { /* the controller went away (driver removed); keep running so the shortcut still works */ }
            }
            try { pad.Submit(default); } catch { }
        }
        finally { Native.timeEndPeriod(1); }
    }

    public void Dispose()
    {
        stopping = true;
        active = false;
        ticker?.Join(500);
        if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        rawWindow?.DestroyHandle();
        rawWindow = null;
    }

    /// <summary>A hidden window that receives the mouse's own movement counts (not pointer positions).</summary>
    sealed class RawMouseWindow : NativeWindow
    {
        readonly Engine engine;

        public RawMouseWindow(Engine engine)
        {
            this.engine = engine;
            CreateHandle(new CreateParams { Parent = Native.HWND_MESSAGE });
            var device = new Native.RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = Handle };
            if (!Native.RegisterRawInputDevices([device], 1, (uint)sizeof(Native.RAWINPUTDEVICE)))
                throw new InvalidOperationException($"Windows refused the mouse movement watch (error {Marshal.GetLastWin32Error()}).");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT)
            {
                uint size = (uint)sizeof(Native.RAWINPUT);
                uint read = Native.GetRawInputData(m.LParam, Native.RID_INPUT, out var input, ref size, (uint)sizeof(Native.RAWINPUTHEADER));
                if (read != uint.MaxValue && input.header.dwType == Native.RIM_TYPEMOUSE
                    && (input.mouse.usFlags & Native.MOUSE_MOVE_ABSOLUTE) == 0
                    && (input.mouse.lLastX != 0 || input.mouse.lLastY != 0))
                    engine.RawMouse(input.mouse.lLastX, input.mouse.lLastY);
            }
            base.WndProc(ref m);
        }
    }
}
