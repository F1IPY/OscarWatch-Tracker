using System.Threading.Channels;
using OscarWatch.Core.Models;
using OscarWatch.Core.Services;

namespace OscarWatch.Core.Ft4;

/// <summary>
/// Queues FT4 spots and posts them to OscarWatch.org. Repeated decodes of the same
/// station, satellite, and slot are sent once.
/// </summary>
public sealed class OscarWatchSpotReporter : IDisposable
{
    private readonly Func<SatelliteStatusSettings, OscarWatchSatelliteSpot, CancellationToken, Task<SatelliteSpotResult>> _submit;
    private readonly Func<bool> _enabled;
    private readonly Func<SatelliteStatusSettings> _connection;
    private readonly Func<int, int, TimeSpan> _retryDelay;
    private readonly Channel<OscarWatchSatelliteSpot> _pending;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private string? _lastNotice;
    private DateTime _lastNoticeUtc = DateTime.MinValue;
    private int _disposed;

    public OscarWatchSpotReporter(
        ISatelliteSpotService spots,
        Func<bool> enabled,
        Func<SatelliteStatusSettings> connection)
        : this(
            (settings, spot, ct) => spots.SubmitAsync(settings, spot, ct),
            enabled,
            connection,
            DefaultRetryDelay)
    {
    }

    public OscarWatchSpotReporter(
        Func<SatelliteStatusSettings, OscarWatchSatelliteSpot, CancellationToken, Task<SatelliteSpotResult>> submit,
        Func<bool> enabled,
        Func<SatelliteStatusSettings> connection,
        Func<int, int, TimeSpan>? retryDelay = null)
    {
        _submit = submit;
        _enabled = enabled;
        _connection = connection;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _pending = Channel.CreateBounded<OscarWatchSatelliteSpot>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(RunAsync);
    }

    /// <summary>Operator-facing diagnostics (message, optional exception) for the host application to log.</summary>
    public event Action<string, Exception?>? Diagnostic;

    public static TimeSpan DefaultRetryDelay(int statusCode, int attempt) =>
        statusCode == 429 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(attempt);

    public bool TryEnqueue(OscarWatchSatelliteSpot spot)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_enabled())
            return false;

        var key = DedupKey(spot);
        lock (_gate)
        {
            PruneSeen();
            if (!_seen.Add(key))
                return false;
        }

        if (_pending.Writer.TryWrite(spot))
            return true;

        lock (_gate)
            _seen.Remove(key);
        Notify("OscarWatch spot queue is full; a decode was not sent.");
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        _pending.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var spot in _pending.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                await SendAsync(spot, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendAsync(OscarWatchSatelliteSpot spot, CancellationToken cancellationToken)
    {
        if (!_enabled())
            return;

        var settings = _connection();
        if (!Ft4OscarWatchSpots.HasApiToken(settings.ApiToken))
        {
            Notify("OscarWatch spots need an API token in Settings, OscarWatch.");
            return;
        }

        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            SatelliteSpotResult result;
            try
            {
                result = await _submit(settings, spot, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Notify($"OscarWatch spot for {spot.HeardCallsign} on {spot.Satellite} was not sent.", ex);
                return;
            }

            if (result.Accepted)
            {
                var recovered = false;
                lock (_gate)
                {
                    recovered = _lastNotice is not null;
                    _lastNotice = null;
                }

                if (recovered)
                    Diagnostic?.Invoke("OscarWatch spots are being accepted again.", null);
                return;
            }

            var retry = result.HttpStatusCode is 0 or 429 || result.HttpStatusCode >= 500;
            if (!retry || attempt == attempts)
            {
                Notify($"OscarWatch spot for {spot.HeardCallsign} on {spot.Satellite} was not accepted: {result.Message}");
                return;
            }

            try
            {
                await Task.Delay(_retryDelay(result.HttpStatusCode, attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Notify(string message, Exception? exception = null)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (exception is null && message == _lastNotice && now - _lastNoticeUtc < TimeSpan.FromMinutes(1))
                return;
            _lastNotice = message;
            _lastNoticeUtc = now;
        }

        Diagnostic?.Invoke(message, exception);
    }

    private void PruneSeen()
    {
        if (_seen.Count < 1000)
            return;

        var cutoff = DateTime.UtcNow.AddHours(-3).Ticks;
        _seen.RemoveWhere(key =>
        {
            var bar = key.IndexOf('|');
            return bar > 0
                && long.TryParse(key.AsSpan(0, bar), out var ticks)
                && ticks < cutoff;
        });
    }

    private static string DedupKey(OscarWatchSatelliteSpot spot) =>
        $"{spot.HeardAtUtc.Ticks}|{spot.Satellite.Trim().ToUpperInvariant()}|{spot.HeardCallsign}";
}
