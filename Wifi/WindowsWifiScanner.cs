using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VoiceWorkbench.Wifi;

public sealed class WindowsWifiScanner
{
    private static readonly Regex PercentRegex = new(@"(?<value>\d+)\s*%", RegexOptions.Compiled);

    public async Task<WifiScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("wlan");
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add("networks");
        startInfo.ArgumentList.Add("mode=bssid");

        Process? process = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));

            process = Process.Start(startInfo);
            if (process is null)
            {
                return new WifiScanResult([], "Could not start Windows Wi-Fi scanner.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var output = await outputTask;
            var error = await errorTask;
            var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(text => !string.IsNullOrWhiteSpace(text)));
            var networks = ParseNetshOutput(combined);
            var status = networks.Count == 0
                ? SimplifyStatus(combined)
                : $"Found {networks.Count} access point signal{(networks.Count == 1 ? "" : "s")}.";
            return new WifiScanResult(networks, status);
        }
        catch (OperationCanceledException)
        {
            return new WifiScanResult([], "Wi-Fi scan timed out.");
        }
        catch (Exception ex)
        {
            return new WifiScanResult([], $"Wi-Fi scan failed: {ex.Message}");
        }
        finally
        {
            KillProcessIfRunning(process);
            process?.Dispose();
        }
    }

    private static IReadOnlyList<WifiNetwork> ParseNetshOutput(string output)
    {
        var networks = new List<WifiNetwork>();
        var ssid = "";
        var authentication = "";
        var encryption = "";
        var bssid = "";
        var signal = 0;
        var radioType = "";
        var band = "";
        var channel = 0;
        var hasBssid = false;

        foreach (var rawLine in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("SSID ", StringComparison.OrdinalIgnoreCase))
            {
                AddCurrent();
                ssid = ReadValue(line);
                authentication = "";
                encryption = "";
                bssid = "";
                signal = 0;
                radioType = "";
                band = "";
                channel = 0;
                hasBssid = false;
                continue;
            }

            if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                authentication = ReadValue(line);
                continue;
            }

            if (line.StartsWith("Encryption", StringComparison.OrdinalIgnoreCase))
            {
                encryption = ReadValue(line);
                continue;
            }

            if (line.StartsWith("BSSID ", StringComparison.OrdinalIgnoreCase))
            {
                AddCurrent();
                bssid = ReadValue(line);
                signal = 0;
                radioType = "";
                band = "";
                channel = 0;
                hasBssid = true;
                continue;
            }

            if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                var match = PercentRegex.Match(line);
                signal = match.Success && int.TryParse(match.Groups["value"].Value, out var parsedSignal)
                    ? parsedSignal
                    : 0;
                continue;
            }

            if (line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
            {
                radioType = ReadValue(line);
                continue;
            }

            if (line.StartsWith("Band", StringComparison.OrdinalIgnoreCase))
            {
                band = ReadValue(line);
                continue;
            }

            if (line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
            {
                channel = int.TryParse(ReadValue(line), out var parsedChannel) ? parsedChannel : 0;
            }
        }

        AddCurrent();
        return networks
            .OrderBy(network => GetBandSort(network))
            .ThenBy(network => network.Channel)
            .ThenByDescending(network => network.SignalPercent)
            .ThenBy(network => network.Ssid)
            .ToList();

        void AddCurrent()
        {
            if (!hasBssid || string.IsNullOrWhiteSpace(bssid))
            {
                return;
            }

            networks.Add(new WifiNetwork
            {
                Ssid = string.IsNullOrWhiteSpace(ssid) ? "(hidden)" : ssid,
                Bssid = bssid,
                SignalPercent = signal,
                Band = band,
                Channel = channel,
                RadioType = radioType,
                Authentication = authentication,
                Encryption = encryption
            });
            hasBssid = false;
        }
    }

    private static string ReadValue(string line)
    {
        var separator = line.IndexOf(':');
        return separator < 0 ? "" : line[(separator + 1)..].Trim();
    }

    private static int GetBandSort(WifiNetwork network)
    {
        if (network.Band.Contains('2') || (network.Channel > 0 && network.Channel <= 14))
        {
            return 0;
        }

        if (network.Band.Contains('5') || (network.Channel >= 32 && network.Channel < 200))
        {
            return 1;
        }

        if (network.Band.Contains('6'))
        {
            return 2;
        }

        return 3;
    }

    private static string SimplifyStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "No Wi-Fi networks returned. The adapter may be off or unavailable.";
        }

        var builder = new StringBuilder();
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
            if (builder.Length > 300)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static void KillProcessIfRunning(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }
        }
        catch
        {
        }
    }
}
