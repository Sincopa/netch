using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Models;

namespace Netch.Servers;

public static class XrayProfileConfig
{
    public static void BindOutboundInterface(JsonObject config, string? interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) || config["outbounds"] is not JsonArray outbounds) return;
        foreach (var outbound in outbounds.OfType<JsonObject>())
        {
            if (outbound["protocol"]?.GetValue<string>() is "blackhole" or "dns" or "loopback") continue;
            var stream = outbound["streamSettings"] as JsonObject;
            if (stream is null) outbound["streamSettings"] = stream = new JsonObject();
            var sockopt = stream["sockopt"] as JsonObject;
            if (sockopt is null) stream["sockopt"] = sockopt = new JsonObject();
            sockopt["interface"] = interfaceName;
        }
    }

    public static async Task<JsonObject> GenerateAsync(Server server, ushort port, string listen)
    {
        JsonObject config;
        if (server.XrayProfile is { } profile)
        {
            config = new JsonObject();
            // Keep provider routing and transport semantics; local listeners/log files are ours.
            foreach (var key in new[] { "outbounds", "routing", "dns", "observatory", "burstObservatory", "policy", "fakedns" })
                if (profile.TryGetProperty(key, out var value)) config[key] = JsonNode.Parse(value.GetRawText());

            var oldTags = profile.TryGetProperty("inbounds", out var inbounds)
                ? inbounds.EnumerateArray().Where(item => item.TryGetProperty("tag", out _))
                    .Select(item => item.GetProperty("tag").GetString()).ToHashSet()
                : new HashSet<string?>();
            if (config["routing"]?["rules"] is JsonArray rules)
                foreach (var rule in rules.OfType<JsonObject>())
                    if (rule["inboundTag"] is JsonArray tags)
                        for (var i = 0; i < tags.Count; i++)
                            if (oldTags.Contains(tags[i]?.GetValue<string>())) tags[i] = "netch-socks";

            // Both spellings occur in subscriptions; the core uses probeURL.
            if (config["observatory"] is JsonObject observer)
            {
                observer.Remove("probeUrl");
                observer["probeURL"] = Global.Settings.LatencyTestUrl;
            }
        }
        else
            config = JsonSerializer.SerializeToNode(await V2rayConfigUtils.GenerateClientConfigAsync(server), Global.NewCustomJsonSerializerOptions())!.AsObject();

        config["inbounds"] = new JsonArray(new JsonObject
        {
            ["tag"] = "netch-socks", ["listen"] = listen, ["port"] = port,
            ["protocol"] = "socks", ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true },
            ["sniffing"] = new JsonObject { ["enabled"] = true, ["destOverride"] = new JsonArray("http", "tls", "quic"), ["routeOnly"] = true }
        });
        config["log"] = new JsonObject { ["loglevel"] = "warning" };
        return config;
    }
}
