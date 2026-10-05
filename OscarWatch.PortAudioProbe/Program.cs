using System.Globalization;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace OscarWatch.PortAudioProbe;

/// <summary>
/// Isolated PortAudio initialiser used by OscarWatch before in-process capture.
/// A native crash here must not take down the main application.
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitInitFailed = 1;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return ProbeInit();

        if (args.Length == 5
            && args[0] is "input" or "output"
            && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceIndex)
            && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleRate)
            && int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channels)
            && int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
        {
            return ProbeOpen(args[0] == "input", deviceIndex, sampleRate, channels, frames);
        }

        Console.Error.WriteLine("Usage: OscarWatch.PortAudioProbe [input|output index sampleRate channels frames]");
        return ExitInitFailed;
    }

    private static int ProbeInit()
    {
        try
        {
            PortAudio.Initialize();

            var inputDevices = 0;
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                if (PortAudio.GetDeviceInfo(i).maxInputChannels > 0)
                    inputDevices++;
            }

            var version = PortAudio.VersionInfo.versionText;
            try
            {
                PortAudio.Terminate();
            }
            catch
            {
                // Init succeeded; a terminate failure still means in-process init is worth attempting.
            }

            Console.WriteLine($"OK inputDevices={inputDevices} version={version}");
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitInitFailed;
        }
    }

    /// <summary>
    /// Open and immediately close one stream. Exit 0 only when that succeeds.
    /// A segfault here is the signal OscarWatch uses to skip the device.
    /// </summary>
    private static int ProbeOpen(bool input, int deviceIndex, int sampleRate, int channels, int frames)
    {
        PaStream? stream = null;
        try
        {
            PortAudio.Initialize();
            if (deviceIndex < 0 || deviceIndex >= PortAudio.DeviceCount)
            {
                Console.Error.WriteLine($"Device index {deviceIndex} is out of range.");
                return ExitInitFailed;
            }

            var info = PortAudio.GetDeviceInfo(deviceIndex);
            var maxChannels = input ? info.maxInputChannels : info.maxOutputChannels;
            if (channels < 1 || channels > maxChannels)
            {
                Console.Error.WriteLine($"Device '{info.name}' does not support {channels} channels.");
                return ExitInitFailed;
            }

            var param = new StreamParameters
            {
                device = deviceIndex,
                channelCount = channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = input ? info.defaultLowInputLatency : info.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            stream = new PaStream(
                inParams: input ? param : null,
                outParams: input ? null : param,
                sampleRate: sampleRate,
                framesPerBuffer: (uint)frames,
                streamFlags: StreamFlags.ClipOff,
                callback: SilentCallback,
                userData: IntPtr.Zero);
            stream.Start();
            stream.Stop();
            Console.WriteLine("OK");
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitInitFailed;
        }
        finally
        {
            if (stream is not null)
            {
                try { stream.Dispose(); } catch { /* open failed */ }
            }

            try { PortAudio.Terminate(); } catch { /* process is exiting */ }
        }
    }

    private static StreamCallbackResult SilentCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData) =>
        StreamCallbackResult.Continue;
}
