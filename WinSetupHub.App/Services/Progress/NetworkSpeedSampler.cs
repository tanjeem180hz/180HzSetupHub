using System.Net.NetworkInformation;

namespace WinSetupHub.App.Services.Progress;

public sealed class NetworkSpeedSampler
{
    private long _lastBytes;
    private DateTime _lastMeasuredAt = DateTime.Now;

    public void Reset()
    {
        _lastBytes = 0;
        _lastMeasuredAt = DateTime.Now;
    }

    public string Read()
    {
        var currentBytes = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Select(ReadBytesReceived)
            .Sum();

        var now = DateTime.Now;
        if (_lastBytes == 0)
        {
            _lastBytes = currentBytes;
            _lastMeasuredAt = now;
            return "Measuring speed";
        }

        var elapsed = Math.Max(0.25, (now - _lastMeasuredAt).TotalSeconds);
        var bytesPerSecond = Math.Max(0, (currentBytes - _lastBytes) / elapsed);

        _lastBytes = currentBytes;
        _lastMeasuredAt = now;

        return bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:0.0} MB/s"
            : $"{bytesPerSecond / 1024:0.0} KB/s";
    }

    private static long ReadBytesReceived(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPv4Statistics().BytesReceived;
        }
        catch
        {
            return 0;
        }
    }
}
