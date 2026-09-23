using System.Text.Json;
using Netch.Application;
using Netch.JsonConverter;
using Netch.Models;
using Netch.Servers;

namespace Netch.Utils;

/// <summary>Imports endpoint definitions, never remote routing rules or local listeners.</summary>
internal static class SubscriptionJsonParser
{
    public static IEnumerable<Server> Parse(JsonElement root)
    {
        var entries = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : new[] { root };
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            IEnumerable<Server> servers;
            try { servers = ParseEntry(entry).ToArray(); }
            catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException or NotSupportedException)
            {
                Log.Warning("Skipped malformed JSON subscription entry ({ErrorType})", exception.GetType().Name);
                continue;
            }
            foreach (var server in servers.Where(CatalogService.HasEndpoint))
            {
                // Full endpoint identity includes credentials and transport, but is never logged.
                var identity = (Server)server.Clone();
                identity.Remark = string.Empty;
                identity.Group = Constants.DefaultGroup;
                if (unique.Add(JsonSerializer.Serialize(identity, identity.GetType()))) yield return server;
            }
        }
    }

    private static IEnumerable<Server> ParseEntry(JsonElement entry)
    {
        if (entry.TryGetProperty("outbounds", out var outbounds) && outbounds.ValueKind == JsonValueKind.Array)
        {
            // One provider profile is one selectable entry. Preserve balancers and every hop.
            var server = outbounds.EnumerateArray().Select(ParseXrayOutbound).FirstOrDefault(value => value is not null);
            if (server is null) yield break;
            server.Remark = Text(entry, "remarks");
            server.XrayProfile = entry.Clone();
            yield return server;
        }
        else if (entry.TryGetProperty("Type", out _))
        {
            var server = entry.Deserialize<Server>(new JsonSerializerOptions
            {
                Converters = { new ServerConverterWithTypeDiscriminator() }
            });
            if (server is not null) yield return server;
        }
        else if (Text(entry, "server").Length > 0 && Number(entry, "server_port") is > 0 and <= 65535 &&
                 Text(entry, "method").Length > 0 && Text(entry, "password").Length > 0)
        {
            yield return new ShadowsocksServer
            {
                Hostname = Text(entry, "server"), Port = (ushort)Number(entry, "server_port"),
                EncryptMethod = Text(entry, "method"), Password = Text(entry, "password"),
                Remark = Text(entry, "remarks"), Plugin = Text(entry, "plugin"), PluginOption = Text(entry, "plugin_opts")
            };
        }
    }

    private static VMessServer? ParseXrayOutbound(JsonElement outbound)
    {
        var protocol = Text(outbound, "protocol");
        if (protocol is not ("vless" or "vmess")) return null;
        var stream = Property(outbound, "streamSettings");
        // These fields describe the UI entry. XrayProfile retains the actual transport and hops.
        var endpoint = First(Property(Property(outbound, "settings"), "vnext"));
        var user = First(Property(endpoint, "users"));
        var address = Text(endpoint, "address");
        var port = Number(endpoint, "port");
        var id = Text(user, "id");
        if (address.Length == 0 || port is < 1 or > 65535 || id.Length == 0) return null;
        VMessServer server = protocol == "vless" ? new VLESSServer() : new VMessServer();
        server.Hostname = address;
        server.Port = (ushort)port;
        server.UserID = id;
        server.AlterID = Number(user, "alterId");
        server.EncryptMethod = Text(user, protocol == "vless" ? "encryption" : "security", protocol == "vless" ? "none" : "auto");
        server.TransferProtocol = Text(stream, "network", "tcp");
        server.TLSSecureType = Text(stream, "security", "none");
        server.UseMux = Property(Property(outbound, "mux"), "enabled").ValueKind == JsonValueKind.True;
        if (server is VLESSServer vless) vless.Flow = Text(user, "flow");
        if (server.TransferProtocol == "http") server.TransferProtocol = "h2";
        var security = Property(stream, server.TLSSecureType + "Settings");
        server.ServerName = Text(security, "serverName");
        server.Fingerprint = Text(security, "fingerprint");
        server.RealityPublicKey = Text(security, "publicKey");
        server.RealityShortId = Text(security, "shortId");
        server.RealitySpiderX = Text(security, "spiderX");
        switch (server.TransferProtocol)
        {
            case "ws":
                var ws = Property(stream, "wsSettings");
                server.Path = Text(ws, "path", "/");
                server.Host = Text(Property(ws, "headers"), "Host");
                break;
            case "grpc":
                var grpc = Property(stream, "grpcSettings");
                server.Path = Text(grpc, "serviceName");
                server.FakeType = Property(grpc, "multiMode").ValueKind == JsonValueKind.True ? "multi" : "gun";
                break;
            case "h2":
                var http = Property(stream, "httpSettings");
                server.Path = Text(http, "path", "/");
                var host = First(Property(http, "host"));
                server.Host = host.ValueKind == JsonValueKind.String ? host.GetString() : string.Empty;
                break;
            case "kcp":
                var kcp = Property(stream, "kcpSettings");
                server.Path = Text(kcp, "seed");
                server.FakeType = Text(Property(kcp, "header"), "type", "none");
                break;
            case "quic":
                var quic = Property(stream, "quicSettings");
                server.QUICSecure = Text(quic, "security", "none");
                server.QUICSecret = Text(quic, "key");
                server.FakeType = Text(Property(quic, "header"), "type", "none");
                break;
        }
        return server;
    }

    private static JsonElement Property(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    private static JsonElement First(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().FirstOrDefault() : default;
    private static string Text(JsonElement value, string key, string fallback = "") =>
        Property(value, key) is var result && result.ValueKind == JsonValueKind.String ? result.GetString() ?? fallback : fallback;
    private static int Number(JsonElement value, string key) =>
        Property(value, key) is var result && result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out var number) ? number : 0;
}
