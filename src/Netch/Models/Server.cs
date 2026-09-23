using System.Net.Sockets;
using System.Text.Json.Serialization;
using Netch.Utils;

namespace Netch.Models;

public abstract class Server : ICloneable
{
    /// <summary>
    ///     延迟
    /// </summary>
    [JsonIgnore]
    public int Delay { get; private set; } = -1;

    /// <summary>
    ///     组
    /// </summary>
    public string Group { get; set; } = Constants.DefaultGroup;

    /// <summary>
    ///     地址
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    ///     端口
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    ///     倍率
    /// </summary>
    public double Rate { get; } = 1.0;

    /// <summary>
    ///     备注
    /// </summary>
    public string Remark { get; set; } = "";

    /// <summary>Complete subscription profile, including automatic outbound selection.</summary>
    public System.Text.Json.JsonElement? XrayProfile { get; set; }

    [JsonIgnore]
    public bool IsAutomatic => XrayProfile is { } profile && profile.TryGetProperty("routing", out var routing) &&
        routing.TryGetProperty("balancers", out var balancers) && balancers.ValueKind == System.Text.Json.JsonValueKind.Array && balancers.GetArrayLength() > 0;

    [JsonIgnore]
    public string? LatencyMethod { get; private set; }

    /// <summary>
    ///     代理类型
    /// </summary>
    [JsonPropertyOrder(int.MinValue)]
    public abstract string Type { get; }

    public object Clone()
    {
        return MemberwiseClone();
    }

    /// <summary>
    ///     获取备注
    /// </summary>
    /// <returns>备注</returns>
    public override string ToString()
    {
        var remark = string.IsNullOrWhiteSpace(Remark) ? $"{Hostname}:{Port}" : Remark;

        var shortName = ServerHelper.GetUtilByTypeName(Type).ShortName;

        return $"[{shortName}][{Group}] {remark}";
    }

    public abstract string MaskedData();

    /// <summary>
    ///     测试延迟
    /// </summary>
    /// <returns>延迟</returns>
    public async Task<int> PingAsync(CancellationToken cancellationToken = default)
    {
        LatencyMethod = Global.Settings.LatencyTestMethod;
        try
        {
            if (LatencyMethod == "http" || IsAutomatic)
            {
                LatencyMethod = "http";
                return Delay = await Netch.Application.ServerLatencyService.MeasureHttpAsync(this, cancellationToken);
            }
            var destination = await DnsUtils.LookupAsync(Hostname);
            if (destination is null) return Delay = -1;
            var samples = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = LatencyMethod == "tcp"
                    ? await Utils.Utils.TCPingAsync(destination, Port, Global.Settings.RequestTimeout, cancellationToken)
                    : await Utils.Utils.ICMPingAsync(destination, Global.Settings.RequestTimeout);
                if (sample >= 0) samples.Add(sample);
            }
            samples.Sort();
            return Delay = samples.Count == 0 ? -1 : samples[samples.Count / 2];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Delay = -1; }
    }
}

public static class ServerExtension
{
    public static async Task<string> AutoResolveHostnameAsync(this Server server, AddressFamily inet = AddressFamily.Unspecified)
    {
        return (await DnsUtils.LookupAsync(server.Hostname, inet))!.ToString();
    }

    public static bool IsInGroup(this Server server) => server.Group is not Constants.DefaultGroup;
}
