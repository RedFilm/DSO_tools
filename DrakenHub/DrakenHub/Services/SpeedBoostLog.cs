using System.Diagnostics;
using System.Text;

namespace DrakenHub.Services;

/// <summary>Append-only diagnostic log for SpeedBoost host/engine issues.</summary>
internal static class SpeedBoostLog
{
    private static readonly object Gate = new();

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrakenHub", "SpeedBoostIpc");

    public static string HostLogPath => Path.Combine(DirectoryPath, "host.log");
    public static string BridgeLogPath => Path.Combine(DirectoryPath, "bridge.log");

    public static void Host(string message, bool includeStack = false)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
            sb.Append(" [T").Append(Environment.CurrentManagedThreadId).Append("] ");
            sb.Append(message);
            try
            {
                var procs = Process.GetProcessesByName("SpeedBoost");
                sb.Append(" | SpeedBoostCount=").Append(procs.Length);
                if (procs.Length > 0)
                {
                    sb.Append(" pids=");
                    sb.Append(string.Join(",", procs.Select(p => p.Id)));
                }
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            }
            catch
            {
                sb.Append(" | SpeedBoostCount=?");
            }

            if (includeStack)
            {
                sb.AppendLine();
                sb.Append(Environment.StackTrace);
            }

            sb.AppendLine();
            lock (Gate)
                File.AppendAllText(HostLogPath, sb.ToString());
        }
        catch
        {
            // never throw from logging
        }
    }
}
