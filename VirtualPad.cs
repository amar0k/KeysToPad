using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace KeysToPad;

/// <summary>The virtual Xbox 360 controller games see. Needs the ViGEmBus driver to be installed.</summary>
internal sealed class VirtualPad : IPad, IDisposable
{
    public const string DriverPage = "https://github.com/nefarius/ViGEmBus/releases/latest";

    readonly ViGEmClient client;
    readonly IXbox360Controller pad;

    public VirtualPad()
    {
        client = new ViGEmClient();
        pad = client.CreateXbox360Controller();
        pad.AutoSubmitReport = false;
        pad.Connect();
    }

    public void Submit(in PadState state)
    {
        pad.SetButtonsFull(state.Buttons);
        pad.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
        pad.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
        pad.SetAxisValue(Xbox360Axis.LeftThumbX, state.LX);
        pad.SetAxisValue(Xbox360Axis.LeftThumbY, state.LY);
        pad.SetAxisValue(Xbox360Axis.RightThumbX, state.RX);
        pad.SetAxisValue(Xbox360Axis.RightThumbY, state.RY);
        pad.SubmitReport();
    }

    public void Dispose()
    {
        try { pad.Disconnect(); } catch { }
        client.Dispose();
    }
}
