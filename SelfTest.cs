namespace KeysToPad;

/// <summary>Built-in checks. None of them opens a window.</summary>
internal static class SelfTest
{
    sealed class Report
    {
        readonly List<string> lines = new();
        int failed;

        public void Check(string name, bool ok, string detail = "")
        {
            lock (lines)
            {
                lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? $"  [{detail}]" : "")}");
                if (!ok) failed++;
            }
        }

        public void Note(string text) { lock (lines) lines.Add($"      {text}"); }

        public int Finish(string? file)
        {
            int checks = lines.Count(l => l.StartsWith("PASS") || l.StartsWith("FAIL"));
            lines.Add(failed == 0 ? $"ALL {checks} CHECKS PASSED" : $"{failed} OF {checks} CHECKS FAILED");
            string text = string.Join(Environment.NewLine, lines);
            if (file != null) File.WriteAllText(file, text + Environment.NewLine);
            try { Console.WriteLine(text); } catch { }
            return failed == 0 ? 0 : 1;
        }
    }

    sealed class RecordingPad : IPad
    {
        public volatile int Reports;
        public PadState Last;
        public int HighestRX;
        public void Submit(in PadState state)
        {
            Last = state;
            if (state.RX > HighestRX) HighestRX = state.RX;
            Reports++;
        }
    }

    static Profile Make(Action<Settings>? change = null)
    {
        var settings = Settings.Parse(Settings.DefaultFile);
        change?.Invoke(settings);
        return Profile.From(settings);
    }

    static void Refused(Report r, string name, Action action)
    {
        try { action(); r.Check(name, false, "it was accepted"); }
        catch (FormatException error) { r.Check(name, true, error.Message.Length > 70 ? error.Message[..70] + "..." : error.Message); }
    }

    const double Dt = 0.002;

    // ---------------------------------------------------------------- mapping and settings

    public static int Run(string? file)
    {
        var r = new Report();
        Profile p;
        try { p = Make(); r.Check("the default settings file reads", true); }
        catch (Exception error) { r.Check("the default settings file reads", false, error.Message); return r.Finish(file); }

        r.Check("shortcut is Ctrl+Alt+G", p.Toggle is { Key: 'G', Ctrl: true, Alt: true, Shift: false, Win: false });
        r.Check("W is left stick up", p.Keys.GetValueOrDefault('W') == Target.LeftStickUp);
        r.Check("LeftShift is the left stick click", p.Keys.GetValueOrDefault(0xA0) == Target.LeftStickClick);
        r.Check("1 is d-pad up", p.Keys.GetValueOrDefault('1') == Target.DpadUp);
        r.Check("left mouse button is RT", p.MouseButtons[(int)MouseInput.Left] == Target.RT);
        r.Check("key names", KeyNames.ParseKey("F5")[0] == 0x74 && KeyNames.ParseKey("Num3")[0] == 0x63 && KeyNames.ParseKey("shift").Length == 2
            && KeyNames.ParseKey("esc")[0] == 0x1B && KeyNames.ParseKey("0x87")[0] == 0x87 && KeyNames.ParseKey("w")[0] == 'W');
        r.Check("one-key shortcut", KeyNames.ParseHotkey("ScrollLock") is { Key: 0x91, Ctrl: false, Alt: false });
        Refused(r, "unknown key name is refused", () => KeyNames.ParseKey("Blah"));
        Refused(r, "unknown button is refused", () => KeyNames.ParseTarget("keys.W", "Z"));
        Refused(r, "a number is not a button", () => KeyNames.ParseTarget("keys.W", "3"));
        Refused(r, "misspelt setting is refused", () => Settings.Parse("{ \"togleHotkey\": \"F9\" }"));
        Refused(r, "broken file is refused", () => Settings.Parse("{ \"keys\": { \"W\": } }"));
        Refused(r, "sensitivity 0 is refused", () => Profile.From(Settings.Parse("{ \"mouse\": { \"sensitivity\": 0 } }")));
        Refused(r, "a plain shortcut key that is also mapped is refused", () => Profile.From(Settings.Parse("{ \"toggleHotkey\": \"W\", \"keys\": { \"W\": \"A\" } }")));

        // ---- keys to sticks and buttons
        var m = new Mapper(p);
        m.Press(Target.LeftStickUp, true);
        var s = m.Tick(Dt);
        r.Check("W pushes the left stick fully up", s.LX == 0 && s.LY == 32767, $"{s.LX},{s.LY}");
        m.Press(Target.LeftStickRight, true);
        s = m.Tick(Dt);
        r.Check("W+D is a diagonal on the circle", Math.Abs(s.LX - 23170) <= 2 && Math.Abs(s.LY - 23170) <= 2, $"{s.LX},{s.LY}");
        m.Press(Target.Walk, true);
        s = m.Tick(Dt);
        r.Check("holding Walk halves it", Math.Abs(s.LX - 11585) <= 2 && Math.Abs(s.LY - 11585) <= 2, $"{s.LX},{s.LY}");
        m.Press(Target.Walk, false); m.Press(Target.LeftStickUp, false); m.Press(Target.LeftStickRight, false);
        s = m.Tick(Dt);
        r.Check("keys released: stick centred", s.LX == 0 && s.LY == 0);
        m.Press(Target.LeftStickLeft, true); m.Press(Target.LeftStickRight, true);
        r.Check("A+D cancel out", m.Tick(Dt).LX == 0);
        m.Reset();

        m.Press(Target.A, true); m.Press(Target.RT, true); m.Press(Target.DpadLeft, true); m.Press(Target.Guide, true);
        s = m.Tick(Dt);
        r.Check("buttons, trigger and d-pad", s.Buttons == (PadButtons.A | PadButtons.DpadLeft | PadButtons.Guide) && s.RightTrigger == 255 && s.LeftTrigger == 0, $"0x{s.Buttons:X4}");
        m.Reset();
        r.Check("reset lets go of everything", m.Tick(Dt).Equals(default));

        m.Press(Target.B, true); m.Press(Target.B, true); m.Press(Target.B, false);
        r.Check("two keys on one button: still held after one is let go", (m.Tick(Dt).Buttons & PadButtons.B) != 0);
        m.Press(Target.B, false); m.Press(Target.B, false);
        m.Press(Target.B, true);
        r.Check("an extra release does no harm", (m.Tick(Dt).Buttons & PadButtons.B) != 0);
        m.Reset();

        m.Pulse(Target.Y);
        int held = 0;
        while ((m.Tick(Dt).Buttons & PadButtons.Y) != 0 && held < 1000) held++;
        r.Check("a wheel notch is a short press", held * Dt is > 0.05 and < 0.07, $"{held * Dt * 1000:0} ms");

        // ---- mouse to stick
        double full = Mapper.FullRate / p.Sensitivity;
        double Shown(short axis) => axis / 32767.0;
        double Push(double shown, Profile q) => shown <= 0 ? 0 : Math.Pow(Math.Max(0, (shown - q.DeadZone) / (1 - q.DeadZone)), 1 / q.Curve);

        m.Reset();
        for (int i = 0; i < 150; i++) { m.MouseMove(3, 0); s = m.Tick(Dt); }           // 1500 counts a second = half the top speed
        double expected = p.DeadZone + (1 - p.DeadZone) * 0.5;
        r.Check("a steady hand speed holds the stick steady", Math.Abs(Shown(s.RX) - expected) < 0.01 && s.RY == 0, $"{Shown(s.RX):0.000} (want {expected:0.000})");
        int rest = 0;
        while (m.Tick(Dt).RX != 0 && rest < 1000) rest++;
        r.Check("the stick comes back to centre when the mouse stops", rest * Dt < 0.08, $"{rest * Dt * 1000:0} ms");

        m.Reset();
        var random = new Random(7);
        double played = 0; int fed = 0;
        for (int i = 0; i < 400; i++)
        {
            int dx = i < 250 ? random.Next(0, 5) : 0;
            fed += dx;
            if (dx != 0) m.MouseMove(dx, 0);
            played += Push(Shown(m.Tick(Dt).RX), p) * full * Dt;
        }
        r.Check("the stick plays back the distance the mouse moved", Math.Abs(played - fed) / fed < 0.02, $"{played:0} of {fed} counts");

        m.Reset(); m.MouseMove(0, -10); s = m.Tick(Dt);
        r.Check("mouse up is stick up", s.RY > 0 && s.RX == 0, $"{s.RX},{s.RY}");
        m.Reset(); m.MouseMove(-10, 0); s = m.Tick(Dt);
        r.Check("mouse left is stick left", s.RX < 0 && s.RY == 0, $"{s.RX},{s.RY}");
        m.Reset(); m.MouseMove(1, 0); s = m.Tick(Dt);
        r.Check("the smallest movement clears the dead zone", Shown(s.RX) > p.DeadZone && Shown(s.RX) < p.DeadZone + 0.05, $"{Shown(s.RX):0.000}");
        m.Reset(); m.MouseMove(1000, 1000); s = m.Tick(Dt);
        r.Check("a fast diagonal stays on the circle", Math.Abs(s.RX - 23170) <= 2 && Math.Abs(s.RY + 23170) <= 2, $"{s.RX},{s.RY}");

        // A flick three times faster than the stick's top speed, for 40 ms.
        int FullTicksAfterFlick(Mapper mapper, int flickTicks)
        {
            mapper.Reset();
            for (int i = 0; i < flickTicks; i++) { mapper.MouseMove(18, 0); mapper.Tick(Dt); }
            int ticks = 0;
            while (mapper.Tick(Dt).RX == 32767 && ticks < 5000) ticks++;
            return ticks;
        }
        int after = FullTicksAfterFlick(m, 20);
        r.Check("after a fast flick the stick stays pushed while it catches up", after * Dt is > 0.05 and < 0.09, $"{after * Dt * 1000:0} ms");
        after = FullTicksAfterFlick(m, 500);
        r.Check("catching up is limited to carryOverMs", Math.Abs(after * Dt - p.CarryOverMs / 1000) < 0.012, $"{after * Dt * 1000:0} ms");
        var noCarry = new Mapper(Make(x => x.Mouse.CarryOverMs = 0));
        after = FullTicksAfterFlick(noCarry, 500);
        r.Check("carryOverMs 0 stops straight away", after <= 2, $"{after * Dt * 1000:0} ms");

        var q = Make(x => { x.Mouse.Sensitivity = 2; x.Mouse.Curve = 0.5; });
        var shaped = new Mapper(q);
        double sum = 0;
        for (int i = 0; i < 300; i++)
        {
            shaped.MouseMove(i % 8 == 0 ? 2 : 1, 0);                                     // about 560 counts a second
            s = shaped.Tick(Dt);
            if (i >= 200) sum += Shown(s.RX);
        }
        double push = 562.5 / (Mapper.FullRate / 2);
        expected = q.DeadZone + (1 - q.DeadZone) * Math.Sqrt(push);
        r.Check("sensitivity and curve shape the stick", Math.Abs(sum / 100 - expected) < 0.03, $"{sum / 100:0.000} (want {expected:0.000})");

        var inverted = new Mapper(Make(x => x.Mouse.InvertY = true));
        inverted.MouseMove(0, -10);
        r.Check("invertY flips up and down", inverted.Tick(Dt).RY < 0);
        var slowY = new Mapper(Make(x => x.Mouse.VerticalRatio = 0.5));
        for (int i = 0; i < 150; i++) { slowY.MouseMove(0, 6); s = slowY.Tick(Dt); }
        expected = -(p.DeadZone + (1 - p.DeadZone) * 0.5);
        r.Check("verticalRatio slows up and down", Math.Abs(Shown(s.RY) - expected) < 0.01, $"{Shown(s.RY):0.000} (want {expected:0.000})");
        var left = new Mapper(Make(x => x.Mouse.Stick = "Left"));
        left.MouseMove(10, 0); s = left.Tick(Dt);
        r.Check("the mouse can drive the left stick instead", s.LX > 0 && s.RX == 0);
        var none = new Mapper(Make(x => x.Mouse.Stick = "None"));
        none.MouseMove(10, 0);
        r.Check("mouse stick None leaves the sticks alone", none.Tick(Dt).Equals(default));

        // ---- which key and mouse events are kept from Windows (the engine's rules, without touching Windows)
        const int F9 = 0x78;
        var recorder = new RecordingPad();
        using (var engine = new Engine(recorder, Make(x => x.ToggleHotkey = "F9")))
        {
            r.Check("off: W goes to Windows as normal", !engine.TestKey('W', true) && !engine.TestKey('W', false));
            r.Check("off: mouse goes to Windows as normal", !engine.TestMouse(Native.WM_MOUSEMOVE, 0) && !engine.TestMouse(Native.WM_LBUTTONDOWN, 0) && !engine.TestMouse(Native.WM_LBUTTONUP, 0));
            r.Check("the shortcut turns controller mode on and is kept from Windows", engine.TestKey(F9, true) && engine.Active && engine.TestKey(F9, false));
            r.Check("on: W is kept and pushes the stick", engine.TestKey('W', true) && engine.TestKey('W', true) && engine.TestTick(Dt).LY == 32767);
            r.Check("on: W release is kept and centres the stick", engine.TestKey('W', false) && engine.TestTick(Dt).LY == 0);
            r.Check("on: a key that is not in the list still reaches Windows", !engine.TestKey('Z', true) && !engine.TestKey('Z', false));
            r.Check("on: pointer movement is held", engine.TestMouse(Native.WM_MOUSEMOVE, 0));
            r.Check("on: left click is RT", engine.TestMouse(Native.WM_LBUTTONDOWN, 0) && engine.TestTick(Dt).RightTrigger == 255);
            r.Check("on: side button (back) is B", engine.TestMouse(Native.WM_XBUTTONDOWN, 1u << 16) && (engine.TestTick(Dt).Buttons & PadButtons.B) != 0 && engine.TestMouse(Native.WM_XBUTTONUP, 1u << 16));
            r.Check("on: wheel is a Y press", engine.TestMouse(Native.WM_MOUSEWHEEL, unchecked((uint)(120 << 16))) && (engine.TestTick(Dt).Buttons & PadButtons.Y) != 0);

            // Switching off while things are held: their releases must not reach Windows as stray "up" events.
            engine.TestKey('D', true);
            r.Check("the shortcut turns controller mode off", engine.TestKey(F9, true) && !engine.Active && engine.TestKey(F9, false));
            r.Check("off: everything on the controller is let go", engine.TestTick(Dt).Equals(default));
            r.Check("a key held through the switch stays ours until released", engine.TestKey('D', true) && engine.TestKey('D', false) && !engine.TestKey('D', true) && !engine.TestKey('D', false));
            r.Check("a button held through the switch stays ours until released", engine.TestMouse(Native.WM_LBUTTONUP, 0) && !engine.TestMouse(Native.WM_LBUTTONDOWN, 0) && !engine.TestMouse(Native.WM_LBUTTONUP, 0));

            // Switching on while a key is held: Windows saw it go down, so Windows must see it come up.
            engine.TestKey('W', true);
            engine.TestKey(F9, true); engine.TestKey(F9, false);
            r.Check("a key held from before stays with Windows until released", engine.Active && !engine.TestKey('W', true) && engine.TestTick(Dt).LY == 0 && !engine.TestKey('W', false));
            r.Check("and works as the stick the next time it is pressed", engine.TestKey('W', true) && engine.TestTick(Dt).LY == 32767 && engine.TestKey('W', false));
        }
        using (var engine = new Engine(recorder, Make(x => { x.ToggleHotkey = "F9"; x.BlockAllKeys = true; })))
        {
            engine.TestKey(F9, true); engine.TestKey(F9, false);
            r.Check("blockAllKeys keeps unlisted keys from Windows too", engine.TestKey('Z', true) && engine.TestKey('Z', false) && engine.TestTick(Dt).Equals(default));
        }
        using (var engine = new Engine(recorder, p))
            r.Check("G alone is not the Ctrl+Alt+G shortcut", !engine.TestKey('G', true) && !engine.Active && !engine.TestKey('G', false));

        return r.Finish(file);
    }

    // ---------------------------------------------------------------- the real keyboard and mouse watch

    /// <summary>
    /// Sends Windows two made-up key presses (F23 and F24, which no program uses) and a small made-up
    /// mouse nudge, and checks they arrive at the controller and are kept from Windows. Takes about a second.
    /// </summary>
    public static int RunHooks(string? file)
    {
        var r = new Report();
        ApplicationConfiguration.Initialize();
        var profile = Make(x => { x.ToggleHotkey = "F23"; x.Keys = new() { ["F24"] = "A" }; x.MouseButtons = new(); x.Beep = false; });
        var recorder = new RecordingPad();
        using var engine = new Engine(recorder, profile);
        engine.Start();
        bool done = false;
        var script = new Thread(() =>
        {
            try
            {
                Thread.Sleep(200);
                Native.GetCursorPos(out var before);
                Key(0x86, true); Key(0x86, false);
                Thread.Sleep(80);
                r.Check("a made-up F23 press turned controller mode on", engine.Active);
                if (!engine.Active)
                {
                    r.Note("Nothing else was tried. (If an administrator window is in front, Windows blocks made-up key presses.)");
                    return;
                }
                Key(0x87, true);
                Thread.Sleep(80);
                r.Check("F24 held: the controller's A is pressed", (recorder.Last.Buttons & PadButtons.A) != 0);
                Key(0x87, false);
                Thread.Sleep(80);
                r.Check("F24 released: A is let go", (recorder.Last.Buttons & PadButtons.A) == 0);
                recorder.HighestRX = 0;
                for (int i = 0; i < 6; i++) { Move(8, 0); Thread.Sleep(4); }
                Thread.Sleep(60);
                Native.GetCursorPos(out var after);
                r.Check("mouse movement pushed the right stick", recorder.HighestRX > 0, $"highest {recorder.HighestRX / 327.67:0}%");
                r.Check("the pointer stayed where it was", after.x == before.x && after.y == before.y, $"{before.x},{before.y} then {after.x},{after.y}");
                Thread.Sleep(200);
                r.Check("the stick came back to centre", recorder.Last.RX == 0);
                Key(0x86, true); Key(0x86, false);
                Thread.Sleep(80);
                r.Check("F23 turned controller mode off again", !engine.Active);
            }
            catch (Exception error) { r.Check("the check ran", false, error.Message); }
            finally { done = true; }
        });
        var exit = new System.Windows.Forms.Timer { Interval = 30 };
        exit.Tick += (_, _) => { if (done) { exit.Stop(); engine.SetActive(false); Application.ExitThread(); } };
        exit.Start();
        script.Start();
        Application.Run();          // Windows calls the watch on this thread while the script runs on the other
        script.Join();
        return r.Finish(file);
    }

    static void Key(int key, bool down)
    {
        var input = new Native.INPUT { type = 1, wVk = (ushort)key, keyFlags = down ? 0u : 2u };
        Native.SendInput(1, [input], 40);
    }

    static void Move(int dx, int dy)
    {
        var input = new Native.INPUT { type = 0, dx = dx, dy = dy, mouseFlags = 1 };
        Native.SendInput(1, [input], 40);
    }

    // ---------------------------------------------------------------- the virtual controller

    /// <summary>Creates the virtual controller, sets it to a known position and reads it back the way a game does.</summary>
    public static int RunPad(string? file)
    {
        var r = new Report();
        VirtualPad pad;
        try { pad = new VirtualPad(); r.Check("the virtual controller was created", true); }
        catch (Exception error)
        {
            r.Check("the virtual controller was created", false, error.Message);
            r.Note("This needs the ViGEmBus driver: " + VirtualPad.DriverPage);
            return r.Finish(file);
        }
        using (pad)
        {
            var want = new PadState { Buttons = PadButtons.A | PadButtons.DpadLeft | PadButtons.RB, LeftTrigger = 255, RightTrigger = 77, LX = 12345, LY = -23456, RX = 32767, RY = -32768 };
            int slot = -1;
            for (int attempt = 0; attempt < 60 && slot < 0; attempt++)
            {
                pad.Submit(want);
                Thread.Sleep(50);
                for (uint i = 0; i < 4; i++)
                {
                    if (Native.XInputGetState(i, out var state) != 0) continue;
                    var g = state.Gamepad;
                    if (g.wButtons == want.Buttons && g.bLeftTrigger == want.LeftTrigger && g.bRightTrigger == want.RightTrigger
                        && g.sThumbLX == want.LX && g.sThumbLY == want.LY && g.sThumbRX == want.RX && g.sThumbRY == want.RY) slot = (int)i;
                }
            }
            r.Check("Windows shows it as an Xbox controller with the position we set", slot >= 0, slot >= 0 ? $"controller {slot + 1}" : "not seen in 3 seconds");
            if (slot >= 0)
            {
                pad.Submit(default);
                Thread.Sleep(100);
                Native.XInputGetState((uint)slot, out var state);
                var g = state.Gamepad;
                r.Check("and letting go reads as centred", g.wButtons == 0 && g.bLeftTrigger == 0 && g.sThumbLX == 0 && g.sThumbRY == 0);
            }
        }
        return r.Finish(file);
    }
}
