using System.Diagnostics;
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

    internal static bool TryRun(out string? errorMessage, TimeSpan? timeout = null)
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
        var stderrText = stderr.Result.Trim();
        if (process.ExitCode == ExitSuccess)
            return true;

        errorMessage = string.IsNullOrWhiteSpace(stderrText)
            ? process.ExitCode switch
            {
                ExitInitFailed => "PortAudio initialisation failed in the probe process.",
                < 0 => "PortAudio probe crashed. If SmartSDR or DAX is running, close it and try again.",
                _ => $"PortAudio probe exited with code {process.ExitCode}."
            }
            : stderrText;

        return false;
    }

    internal static string? ResolveProbePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ProbeExecutableName);
        return File.Exists(path) ? path : null;
    }
}
