using System.IO.Ports;
using OscarWatch.Core.Ft4;
using OscarWatch.Core.Hardware;
using OscarWatch.Core.Services;
using OscarWatch.Rig;
using Serilog;

namespace OscarWatch.Ft4;

/// <summary>Keys the uplink for FT4 using CAT, handshake lines, VOX, or a manual prompt.</summary>
public sealed class Ft4PttKeyer : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<Ft4PttKeyer>();

    private readonly IRigController _rig;
    private readonly ISettingsService _settings;
    private SerialPort? _separatePort;
    private bool _keyed;
    private Action<string>? _manualPrompt;

    public Ft4PttKeyer(IRigController rig, ISettingsService settings)
    {
        _rig = rig;
        _settings = settings;
    }

    public void SetManualPromptHandler(Action<string>? handler) => _manualPrompt = handler;

    public Ft4PttMethod Method => _settings.Current.Ft4.PttMethod;

    public async Task KeyAsync(CancellationToken cancellationToken = default)
    {
        if (_keyed)
            return;

        var ft4 = _settings.Current.Ft4;
        switch (ft4.PttMethod)
        {
            case Ft4PttMethod.Vox:
                _keyed = true;
                return;

            case Ft4PttMethod.Cat:
                _rig.SetPtt(true);
                _keyed = true;
                break;

            case Ft4PttMethod.CatPortHandshake:
                _rig.SetHandshakePtt(ft4.PttLine == Ft4PttLine.Rts, assert: !ft4.PttInvert);
                _keyed = true;
                break;

            case Ft4PttMethod.SeparateComPort:
                EnsureSeparatePort(ft4);
                SetLine(_separatePort!, ft4.PttLine, assert: !ft4.PttInvert);
                _keyed = true;
                break;

            case Ft4PttMethod.Manual:
                _manualPrompt?.Invoke("key");
                _keyed = true;
                break;
        }

        var lead = Math.Clamp(ft4.PttLeadMs, 0, 2000);
        if (lead > 0)
            await Task.Delay(lead, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnkeyAsync(CancellationToken cancellationToken = default)
    {
        if (!_keyed)
            return;

        var ft4 = _settings.Current.Ft4;
        var tail = Math.Clamp(ft4.PttTailMs, 0, 2000);
        if (tail > 0)
        {
            try
            {
                await Task.Delay(tail, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Halt still has to drop PTT. The tail must not leave the radio keyed.
            }
        }

        DropPtt();
    }

    /// <summary>Drop PTT at once, without the tail delay. Used when the operator stops a transmission.</summary>
    public void UnkeyNow() => DropPtt();

    private void DropPtt()
    {
        if (!_keyed)
            return;

        var ft4 = _settings.Current.Ft4;
        try
        {
            switch (ft4.PttMethod)
            {
                case Ft4PttMethod.Vox:
                    break;
                case Ft4PttMethod.Cat:
                    _rig.SetPtt(false);
                    break;
                case Ft4PttMethod.CatPortHandshake:
                    _rig.SetHandshakePtt(ft4.PttLine == Ft4PttLine.Rts, assert: ft4.PttInvert);
                    break;
                case Ft4PttMethod.SeparateComPort:
                    if (_separatePort is { IsOpen: true })
                        SetLine(_separatePort, ft4.PttLine, assert: ft4.PttInvert);
                    break;
                case Ft4PttMethod.Manual:
                    _manualPrompt?.Invoke("unkey");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FT4 PTT unkey failed");
        }
        finally
        {
            _keyed = false;
        }
    }

    private void EnsureSeparatePort(Ft4Settings ft4)
    {
        var portName = ft4.SeparatePttPort?.Trim() ?? "";
        if (portName.Length == 0)
            throw new InvalidOperationException("Separate PTT COM port is not configured.");

        if (SerialPortConflictHelper.TryDescribeFt4SeparatePttConflict(
                portName,
                _settings.Current.Rig,
                _settings.Current.Rotator,
                _settings.Current.Gps,
                out var conflict))
        {
            throw new InvalidOperationException(conflict);
        }

        if (_separatePort is { IsOpen: true }
            && string.Equals(_separatePort.PortName, portName, StringComparison.OrdinalIgnoreCase))
            return;

        _separatePort?.Dispose();
        _separatePort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false
        };
        _separatePort.Open();
    }

    private static void SetLine(SerialPort port, Ft4PttLine line, bool assert)
    {
        if (line == Ft4PttLine.Rts)
            port.RtsEnable = assert;
        else
            port.DtrEnable = assert;
    }

    public void Dispose()
    {
        try
        {
            if (_keyed)
                UnkeyAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // ignore
        }

        try
        {
            _separatePort?.Dispose();
        }
        catch
        {
            // ignore
        }

        _separatePort = null;
    }
}
