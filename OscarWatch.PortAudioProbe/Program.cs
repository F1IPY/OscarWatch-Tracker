using PortAudioSharp;

namespace OscarWatch.PortAudioProbe;

/// <summary>
/// Isolated PortAudio initialiser used by OscarWatch before in-process capture.
/// A native crash here must not take down the main application.
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitInitFailed = 1;

    public static int Main()
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
}
