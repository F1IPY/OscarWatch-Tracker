using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OscarWatch.Recording;

/// <summary>
/// Runs PortAudio initialisation in a separate process so native SmartSDR/DAX failures
/// cannot terminate OscarWatch.
/// </summary>
internal static class PortAudioOutOfProcessProbe
{
    internal const int ExitSuccess = 0;
    internal const int ExitInitFailed = 1;

    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    internal static string ProbeExecutableName =>
        OperatingSystem.IsWindows()
            ? "OscarWatch.PortAudioProbe.exe"
            : "OscarWatch.PortAudioProbe";

    /// <summary>
    /// Open one stream in the probe process. A segfault inside PortAudio must not kill OscarWatch.
    /// </summary>
    internal static bool TryOpenStream(
        bool input,
        int deviceIndex,
        int sampleRate,
        int channels,
        int framesPerBuffer,
        out string? errorMessage,
        TimeSpan? timeout = null)
    {
        var args = new[]
        {
            input ? "input" : "output",
            deviceIndex.ToString(CultureInfo.InvariantCulture),
            sampleRate.ToString(CultureInfo.InvariantCulture),
            channels.ToString(CultureInfo.InvariantCulture),
            framesPerBuffer.ToString(CultureInfo.InvariantCulture),
        };
        return TryRun(out errorMessage, timeout ?? TimeSpan.FromSeconds(8), args);
    }

    internal static bool TryRun(out string? errorMessage, TimeSpan? timeout = null, IReadOnlyList<string>? arguments = null)
    {
        errorMessage = null;
        var probePath = ResolveProbePath();
        if (probePath is null)
        {
            errorMessage = "PortAudio probe executable was not found next to OscarWatch.";
            return false;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = probePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };
        if (arguments is not null)
        {
            foreach (var arg in arguments)
                process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            if (!process.Start())
            {
                errorMessage = "PortAudio probe process did not start.";
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var waitMs = (int)(timeout ?? DefaultTimeout).TotalMilliseconds;
        if (!process.WaitForExit(waitMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort — the hung probe must not block recording forever.
            }

            errorMessage =
                "PortAudio probe timed out. If SmartSDR or DAX is running, close it and try again.";
            return false;
        }

        Task.WaitAll([stdout, stderr]);
        var stderrText = Tail(stderr.Result.Trim());
        if (process.ExitCode == ExitSuccess)
            return true;

        // 139 = 128 + SIGSEGV, 134 = 128 + SIGABRT. PortAudio's ALSA dmix path uses both.
        errorMessage = process.ExitCode switch
        {
            ExitInitFailed when string.IsNullOrWhiteSpace(stderrText) =>
                "PortAudio initialisation failed in the probe process.",
            ExitInitFailed => stderrText,
            139 or 134 or < 0 =>
                "PortAudio probe crashed while opening the sound device.",
            _ when !string.IsNullOrWhiteSpace(stderrText) => stderrText,
            _ => $"PortAudio probe exited with code {process.ExitCode}."
        };

        return false;
    }

    private static string Tail(string text)
    {
        const int max = 500;
        if (text.Length <= max)
            return text;
        return text[^max..];
    }

    internal static string? ResolveProbePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ProbeExecutableName);
        return File.Exists(path) ? path : null;
    }
}
