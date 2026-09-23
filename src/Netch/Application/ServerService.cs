using Netch.Models;
using Netch.Servers;
using Netch.Utils;

namespace Netch.Application;

public sealed class ServerService
{
    private const int MaxImportLength = 1_000_000;
    private const int MaxImportCount = 2_000;
    private readonly CatalogService _catalog;
    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public ServerService(CatalogService catalog, ConnectionService connection)
    {
        _catalog = catalog;
        _connection = connection;
    }

    public ServerCatalogDto GetCatalog(int importedCount = 0)
    {
        return new ServerCatalogDto(_catalog.GetServers(), _catalog.GetSelectedServerId(), importedCount);
    }

    public ServerEditorDocumentDto GetEditorDocument()
    {
        return new ServerEditorDocumentDto(
            Global.Settings.Server.Select((server, index) => ToEditorDto(server, index)).ToArray(),
            new[] { "SS", "SSR", "SOCKS", "Trojan", "VMess", "VLESS", "SSH", "WireGuard" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["ss.encryptMethod"] = SSGlobal.EncryptMethods,
                ["ssr.encryptMethod"] = SSRGlobal.EncryptMethods,
                ["ssr.protocol"] = SSRGlobal.Protocols,
                ["ssr.obfs"] = SSRGlobal.OBFSs,
                ["socks.version"] = SOCKSGlobal.Versions,
                ["vmess.encryptMethod"] = VMessGlobal.EncryptMethods,
                ["v2.transferProtocol"] = VMessGlobal.TransferProtocols,
                ["v2.packetEncoding"] = VMessGlobal.PacketEncodings,
                ["v2.fakeType"] = VMessGlobal.FakeTypes,
                ["v2.quicSecure"] = VMessGlobal.QUIC,
                ["vmess.tls"] = VMessGlobal.TLSSecure,
                ["vless.tls"] = VLESSGlobal.TLSSecure,
                ["vless.fingerprint"] = VLESSGlobal.Fingerprints,
                ["boolean.inherit"] = new[] { "", "true", "false" }
            });
    }

    public async Task<ServerCatalogDto> SaveDetailsAsync(ServerDetailsWriteRequest request, CancellationToken cancellationToken = default)
    {
        var protocol = NormalizeProtocol(request.Protocol);
        var name = NormalizeName(request.Name);
        var hostname = NormalizeHostname(request.Hostname);
        ValidateProtocolFields(protocol, request);
        if (request.Port is < 1 or > 65535)
            throw new AppException("INVALID_SERVER_PORT", "Server port must be between 1 and 65535.");

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var group = NormalizeGroup(request.Group, Global.Settings.Subscription.Select(value => value.Remark));
            var previousServers = Global.Settings.Server.ToList();
            var previousSelection = Global.Settings.ServerComboBoxSelectedIndex;
            var previousFavorites = GetFavoriteKeys().ToList();
            var previousProfileNames = Global.Settings.Profiles.Select(profile => profile.ServerRemark).ToArray();
            Server replacement;
            Server? existing = null;

            if (request.Id is null)
                replacement = CreateServer(protocol);
            else
            {
                existing = _catalog.ResolveServer(request.Id);
                EnsureEditable(existing);
                if (!string.Equals(existing.Type, protocol, StringComparison.Ordinal))
                    throw new AppException("SERVER_PROTOCOL_LOCKED", "The protocol of an existing server cannot be changed.");
                replacement = (Server)existing.Clone();
            }

            replacement.Remark = name;
            replacement.Group = group;
            replacement.Hostname = hostname;
            replacement.Port = (ushort)request.Port;
            ApplyProtocolValues(replacement, request, existing is null);

            try
            {
                if (existing is null)
                {
                    Global.Settings.Server.Add(replacement);
                    Global.Settings.ServerComboBoxSelectedIndex = Global.Settings.Server.Count - 1;
                }
                else
                {
                    var index = Global.Settings.Server.IndexOf(existing);
                    var oldKey = ServerIdentity.GetFavoriteKey(existing);
                    var favorite = previousFavorites.Contains(oldKey, StringComparer.Ordinal);
                    Global.Settings.Server[index] = replacement;
                    if (favorite)
                    {
                        GetFavoriteKeys().RemoveAll(value => string.Equals(value, oldKey, StringComparison.Ordinal));
                        AddFavoriteKey(ServerIdentity.GetFavoriteKey(replacement));
                    }
                    foreach (var profile in Global.Settings.Profiles.Where(profile => profile.ServerRemark == existing.Remark))
                        profile.ServerRemark = replacement.Remark;
                    if (previousSelection == index)
                        Global.Settings.ServerComboBoxSelectedIndex = index;
                }
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Global.Settings.Server.Clear();
                Global.Settings.Server.AddRange(previousServers);
                Global.Settings.ServerComboBoxSelectedIndex = previousSelection;
                RestoreFavoriteKeys(previousFavorites);
                for (var index = 0; index < Math.Min(previousProfileNames.Length, Global.Settings.Profiles.Count); index++)
                    Global.Settings.Profiles[index].ServerRemark = previousProfileNames[index];
                if (exception is AppException)
                    throw;
                throw new AppException("SERVER_SAVE_FAILED", "Server details could not be saved.", innerException: exception);
            }

            return PublishChanged();
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<ServerCatalogDto> ImportAsync(ServerImportRequest request, CancellationToken cancellationToken = default)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxImportLength)
            throw new AppException("INVALID_SERVER_IMPORT", "Paste between 1 and 1,000,000 characters of share-link data.");

        var parsed = await Task.Run(() => ShareLink.ParseText(text), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (parsed.Count == 0)
            throw new AppException("SERVER_IMPORT_EMPTY", "No supported server links were found.");
        if (parsed.Count > MaxImportCount)
            throw new AppException("SERVER_IMPORT_TOO_LARGE", $"A single import may contain at most {MaxImportCount} servers.");

        foreach (var server in parsed)
        {
            ValidateHostname(server.Hostname);
            if (server.Port == 0)
                throw new AppException("INVALID_SERVER", "An imported server contains an invalid port.");
        }

        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var group = NormalizeGroup(request.Group, Global.Settings.Subscription.Select(value => value.Remark));
            foreach (var server in parsed)
                server.Group = group;
            var originalCount = Global.Settings.Server.Count;
            var previousSelection = Global.Settings.ServerComboBoxSelectedIndex;
            Global.Settings.Server.AddRange(parsed);
            if (Global.Settings.ServerComboBoxSelectedIndex < 0)
                Global.Settings.ServerComboBoxSelectedIndex = 0;

            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Global.Settings.Server.RemoveRange(originalCount, parsed.Count);
                Global.Settings.ServerComboBoxSelectedIndex = previousSelection;
                throw new AppException("SERVER_IMPORT_FAILED", "Imported servers could not be saved.", innerException: exception);
            }

            return PublishChanged(parsed.Count);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<ServerCatalogDto> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await _connection.AcquireInactiveLeaseAsync(cancellationToken);
            var server = _catalog.ResolveServer(id);
            EnsureEditable(server);
            var index = Global.Settings.Server.IndexOf(server);
            var previousSelection = Global.Settings.ServerComboBoxSelectedIndex;
            var previousFavorites = GetFavoriteKeys().ToList();
            var favoriteKey = ServerIdentity.GetFavoriteKey(server);
            Global.Settings.Server.RemoveAt(index);
            if (!Global.Settings.Server.Any(value => string.Equals(ServerIdentity.GetFavoriteKey(value), favoriteKey, StringComparison.Ordinal)))
                GetFavoriteKeys().RemoveAll(value => string.Equals(value, favoriteKey, StringComparison.Ordinal));
            Global.Settings.ServerComboBoxSelectedIndex = AdjustSelectedIndex(
                previousSelection,
                index,
                Global.Settings.Server.Count);

            try
            {
                await Configuration.SaveAsync();
            }
            catch (Exception exception)
            {
                Global.Settings.Server.Insert(index, server);
                Global.Settings.ServerComboBoxSelectedIndex = previousSelection;
                RestoreFavoriteKeys(previousFavorites);
                throw new AppException("SERVER_DELETE_FAILED", "The server could not be deleted.", innerException: exception);
            }

            return PublishChanged();
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<ServerCatalogDto> SetFavoriteAsync(ServerFavoriteRequest request, CancellationToken cancellationToken = default)
    {
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var server = _catalog.ResolveServer(request.Id);
            var key = ServerIdentity.GetFavoriteKey(server);
            var favorites = GetFavoriteKeys();
            if (request.IsFavorite)
                AddFavoriteKey(key);
            else
                favorites.RemoveAll(value => string.Equals(value, key, StringComparison.Ordinal));

            await Configuration.SaveAsync();
            return PublishChanged();
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public static int AdjustSelectedIndex(int selectedIndex, int removedIndex, int remainingCount)
    {
        if (remainingCount <= 0)
            return -1;
        if (selectedIndex < 0)
            return 0;
        if (selectedIndex > removedIndex)
            return selectedIndex - 1;
        if (selectedIndex == removedIndex)
            return Math.Min(removedIndex, remainingCount - 1);
        return Math.Min(selectedIndex, remainingCount - 1);
    }

    public static string NormalizeName(string? value)
    {
        return NormalizeVisible(value, "Server name", 128, "INVALID_SERVER_NAME");
    }

    public static string NormalizeProtocol(string? value)
    {
        return value switch
        {
            "SS" or "SSR" or "SOCKS" or "Trojan" or "VMess" or "VLESS" or "SSH" or "WireGuard" => value,
            _ => throw new AppException("INVALID_SERVER_PROTOCOL", "Select a supported server protocol.")
        };
    }

    public static void ValidateProtocolFields(string protocol, ServerDetailsWriteRequest request)
    {
        var valueKeys = protocol switch
        {
            "SS" => new[] { "encryptMethod", "plugin", "pluginOption" },
            "SSR" => new[] { "encryptMethod", "protocol", "protocolParam", "obfs", "obfsParam" },
            "SOCKS" => new[] { "username", "remoteHostname", "version" },
            "Trojan" => new[] { "host", "tlsSecureType" },
            "VMess" => new[] { "serverName", "transferProtocol", "packetEncoding", "fakeType", "host", "path", "quicSecure", "useMux", "tlsSecureType", "alterId", "encryptMethod" },
            "VLESS" => new[] { "serverName", "transferProtocol", "packetEncoding", "fakeType", "host", "path", "quicSecure", "useMux", "tlsSecureType", "encryptMethod", "fingerprint", "realityPublicKey", "realityShortId", "realitySpiderX" },
            "SSH" => new[] { "user", "publicKey" },
            "WireGuard" => new[] { "localAddresses", "peerPublicKey", "mtu" },
            _ => throw new AppException("INVALID_SERVER_PROTOCOL", "Select a supported server protocol.")
        };
        var secretKeys = protocol switch
        {
            "SS" or "SSR" or "SOCKS" or "Trojan" => new[] { "password" },
            "VMess" or "VLESS" => new[] { "userId", "quicSecret" },
            "SSH" => new[] { "password", "privateKey" },
            "WireGuard" => new[] { "privateKey", "preSharedKey" },
            _ => Array.Empty<string>()
        };

        if (request.Values?.Keys.Any(key => !valueKeys.Contains(key, StringComparer.Ordinal)) == true ||
            request.Secrets?.Keys.Any(key => !secretKeys.Contains(key, StringComparer.Ordinal)) == true ||
            request.ClearSecrets?.Any(key => !secretKeys.Contains(key, StringComparer.Ordinal)) == true)
            throw new AppException("INVALID_SERVER_FIELD", "The server request contains a field that is not valid for the selected protocol.");

        if (request.ClearSecrets?.Distinct(StringComparer.Ordinal).Count() != request.ClearSecrets?.Count ||
            request.ClearSecrets?.Any(key => request.Secrets?.TryGetValue(key, out var value) == true && !string.IsNullOrEmpty(value)) == true)
            throw new AppException("INVALID_SERVER_FIELD", "A secret cannot be supplied and cleared in the same request.");
    }

    public static string NormalizeGroup(string? value, IEnumerable<string> subscriptionNames)
    {
        var group = string.IsNullOrWhiteSpace(value)
            ? Constants.DefaultGroup
            : NormalizeVisible(value, "Server group", 64, "INVALID_SERVER_GROUP");
        if (string.Equals(group, Constants.DefaultGroup, StringComparison.OrdinalIgnoreCase))
            return Constants.DefaultGroup;
        if (!string.Equals(group, Constants.DefaultGroup, StringComparison.OrdinalIgnoreCase) &&
            subscriptionNames.Any(name => string.Equals(name, group, StringComparison.OrdinalIgnoreCase)))
            throw new AppException("SERVER_GROUP_RESERVED", "This group name belongs to a subscription.");
        return group;
    }

    private ServerCatalogDto PublishChanged(int importedCount = 0)
    {
        var catalog = GetCatalog(importedCount);
        AppEvents.Publish("servers.changed", catalog);
        AppEvents.Publish("selection.changed", new
        {
            serverId = catalog.SelectedServerId,
            modeId = _catalog.GetSelectedModeId()
        });
        return catalog;
    }

    private static string NormalizeHostname(string? value)
    {
        var hostname = value?.Trim() ?? string.Empty;
        ValidateHostname(hostname);
        return hostname;
    }

    private static void ValidateHostname(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
            throw new AppException("INVALID_SERVER_HOST", "Server address must be a valid hostname or IP address.");
    }

    private static string NormalizeVisible(string? value, string label, int maxLength, string code)
    {
        var result = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(result) || result.Length > maxLength || result.Any(char.IsControl))
            throw new AppException(code, $"{label} must contain 1 to {maxLength} visible characters.");
        return result;
    }

    private static void EnsureEditable(Server server)
    {
        if (!string.Equals(server.Group, Constants.DefaultGroup, StringComparison.OrdinalIgnoreCase) &&
            Global.Settings.Subscription.Any(value => string.Equals(value.Remark, server.Group, StringComparison.Ordinal)))
            throw new AppException("SUBSCRIPTION_SERVER_READ_ONLY", "Manage this server through its subscription.");
    }

    private static Server CreateServer(string protocol)
    {
        return protocol switch
        {
            "SS" => new ShadowsocksServer(),
            "SSR" => new ShadowsocksRServer(),
            "SOCKS" => new Socks5Server(),
            "Trojan" => new TrojanServer(),
            "VMess" => new VMessServer(),
            "VLESS" => new VLESSServer(),
            "SSH" => new SSHServer(),
            "WireGuard" => new WireGuardServer(),
            _ => throw new InvalidOperationException()
        };
    }

    private static ServerEditorDto ToEditorDto(Server server, int index)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var secrets = new Dictionary<string, bool>(StringComparer.Ordinal);
        switch (server)
        {
            case ShadowsocksServer ss:
                values["encryptMethod"] = ss.EncryptMethod;
                values["plugin"] = ss.Plugin;
                values["pluginOption"] = ss.PluginOption;
                secrets["password"] = HasSecret(ss.Password);
                break;
            case ShadowsocksRServer ssr:
                values["encryptMethod"] = ssr.EncryptMethod;
                values["protocol"] = ssr.Protocol;
                values["protocolParam"] = ssr.ProtocolParam;
                values["obfs"] = ssr.OBFS;
                values["obfsParam"] = ssr.OBFSParam;
                secrets["password"] = HasSecret(ssr.Password);
                break;
            case Socks5Server socks:
                values["username"] = socks.Username;
                values["remoteHostname"] = socks.RemoteHostname;
                values["version"] = socks.Version;
                secrets["password"] = HasSecret(socks.Password);
                break;
            case TrojanServer trojan:
                values["host"] = trojan.Host;
                values["tlsSecureType"] = trojan.TLSSecureType;
                secrets["password"] = HasSecret(trojan.Password);
                break;
            case VLESSServer vless:
                AddV2Values(values, vless);
                values["fingerprint"] = vless.Fingerprint;
                values["realityPublicKey"] = vless.RealityPublicKey;
                values["realityShortId"] = vless.RealityShortId;
                values["realitySpiderX"] = vless.RealitySpiderX;
                secrets["userId"] = HasSecret(vless.UserID);
                secrets["quicSecret"] = HasSecret(vless.QUICSecret);
                break;
            case VMessServer vmess:
                AddV2Values(values, vmess);
                values["alterId"] = vmess.AlterID.ToString();
                values["encryptMethod"] = vmess.EncryptMethod;
                secrets["userId"] = HasSecret(vmess.UserID);
                secrets["quicSecret"] = HasSecret(vmess.QUICSecret);
                break;
            case SSHServer ssh:
                values["user"] = ssh.User;
                values["publicKey"] = ssh.PublicKey;
                secrets["password"] = HasSecret(ssh.Password);
                secrets["privateKey"] = HasSecret(ssh.PrivateKey);
                break;
            case WireGuardServer wireGuard:
                values["localAddresses"] = wireGuard.LocalAddresses;
                values["peerPublicKey"] = wireGuard.PeerPublicKey;
                values["mtu"] = wireGuard.MTU.ToString();
                secrets["privateKey"] = HasSecret(wireGuard.PrivateKey);
                secrets["preSharedKey"] = HasSecret(wireGuard.PreSharedKey);
                break;
            default:
                throw new InvalidOperationException($"Unsupported server type {server.Type}.");
        }

        var managed = !string.Equals(server.Group, Constants.DefaultGroup, StringComparison.OrdinalIgnoreCase) &&
                      Global.Settings.Subscription.Any(value => string.Equals(value.Remark, server.Group, StringComparison.Ordinal));
        return new ServerEditorDto(index.ToString(), server.Remark, server.Group, server.Hostname, server.Port, server.Type, managed, values, secrets);
    }

    private static void AddV2Values(IDictionary<string, string?> values, VMessServer server)
    {
        values["serverName"] = server.ServerName;
        values["transferProtocol"] = server.TransferProtocol;
        values["packetEncoding"] = server.PacketEncoding;
        values["fakeType"] = server.FakeType;
        values["host"] = server.Host;
        values["path"] = server.Path;
        values["quicSecure"] = server.QUICSecure;
        values["useMux"] = server.UseMux?.ToString().ToLowerInvariant() ?? string.Empty;
        values["tlsSecureType"] = server.TLSSecureType;
    }

    private static void ApplyProtocolValues(Server server, ServerDetailsWriteRequest request, bool creating)
    {
        switch (server)
        {
            case ShadowsocksServer ss:
                ss.EncryptMethod = Choice(request, "encryptMethod", ss.EncryptMethod, SSGlobal.EncryptMethods);
                ss.Plugin = Optional(request, "plugin", ss.Plugin);
                ss.PluginOption = Optional(request, "pluginOption", ss.PluginOption);
                ss.Password = Secret(request, "password", ss.Password, creating, required: true);
                break;
            case ShadowsocksRServer ssr:
                ssr.EncryptMethod = Choice(request, "encryptMethod", ssr.EncryptMethod, SSRGlobal.EncryptMethods);
                ssr.Protocol = Choice(request, "protocol", ssr.Protocol, SSRGlobal.Protocols);
                ssr.ProtocolParam = Optional(request, "protocolParam", ssr.ProtocolParam);
                ssr.OBFS = Choice(request, "obfs", ssr.OBFS, SSRGlobal.OBFSs);
                ssr.OBFSParam = Optional(request, "obfsParam", ssr.OBFSParam);
                ssr.Password = Secret(request, "password", ssr.Password, creating, required: true);
                break;
            case Socks5Server socks:
                socks.Username = Optional(request, "username", socks.Username);
                socks.RemoteHostname = Optional(request, "remoteHostname", socks.RemoteHostname);
                socks.Version = Choice(request, "version", socks.Version, SOCKSGlobal.Versions);
                socks.Password = Secret(request, "password", socks.Password, creating, required: false);
                break;
            case TrojanServer trojan:
                trojan.Host = Optional(request, "host", trojan.Host);
                trojan.TLSSecureType = Choice(request, "tlsSecureType", trojan.TLSSecureType, VLESSGlobal.TLSSecure);
                trojan.Password = Secret(request, "password", trojan.Password, creating, required: true);
                break;
            case VLESSServer vless:
                ApplyV2Values(vless, request, VLESSGlobal.TLSSecure);
                vless.EncryptMethod = Optional(request, "encryptMethod", vless.EncryptMethod) ?? "none";
                vless.Fingerprint = Choice(request, "fingerprint", vless.Fingerprint ?? string.Empty, VLESSGlobal.Fingerprints);
                vless.RealityPublicKey = Optional(request, "realityPublicKey", vless.RealityPublicKey, 4096);
                vless.RealityShortId = Optional(request, "realityShortId", vless.RealityShortId, 256);
                vless.RealitySpiderX = Optional(request, "realitySpiderX", vless.RealitySpiderX, 2048);
                vless.UserID = UuidSecret(request, "userId", vless.UserID, creating);
                vless.QUICSecret = Secret(request, "quicSecret", vless.QUICSecret, creating, required: false);
                break;
            case VMessServer vmess:
                ApplyV2Values(vmess, request, VMessGlobal.TLSSecure);
                vmess.AlterID = Integer(request, "alterId", vmess.AlterID, 0, int.MaxValue);
                vmess.EncryptMethod = Choice(request, "encryptMethod", vmess.EncryptMethod, VMessGlobal.EncryptMethods);
                vmess.UserID = UuidSecret(request, "userId", vmess.UserID, creating);
                vmess.QUICSecret = Secret(request, "quicSecret", vmess.QUICSecret, creating, required: false);
                break;
            case SSHServer ssh:
                ssh.User = Required(request, "user", ssh.User, 256);
                ssh.PublicKey = Optional(request, "publicKey", ssh.PublicKey, 32768, multiline: true);
                ssh.Password = Secret(request, "password", ssh.Password, creating, required: false);
                ssh.PrivateKey = Secret(request, "privateKey", ssh.PrivateKey, creating, required: false, multiline: true);
                if (!HasSecret(ssh.Password) && !HasSecret(ssh.PrivateKey))
                    throw new AppException("SERVER_SECRET_REQUIRED", "SSH requires a password or private key.");
                break;
            case WireGuardServer wireGuard:
                wireGuard.LocalAddresses = Required(request, "localAddresses", wireGuard.LocalAddresses, 2048);
                wireGuard.PeerPublicKey = Required(request, "peerPublicKey", wireGuard.PeerPublicKey, 4096);
                wireGuard.MTU = Integer(request, "mtu", wireGuard.MTU, 576, 9000);
                wireGuard.PrivateKey = Secret(request, "privateKey", wireGuard.PrivateKey, creating, required: true);
                wireGuard.PreSharedKey = Secret(request, "preSharedKey", wireGuard.PreSharedKey, creating, required: false);
                break;
            default:
                throw new AppException("INVALID_SERVER_PROTOCOL", "This server protocol is not supported by the editor.");
        }
    }

    private static void ApplyV2Values(VMessServer server, ServerDetailsWriteRequest request, IReadOnlyList<string> tlsOptions)
    {
        server.ServerName = Optional(request, "serverName", server.ServerName);
        server.TransferProtocol = Choice(request, "transferProtocol", server.TransferProtocol, VMessGlobal.TransferProtocols);
        server.PacketEncoding = Choice(request, "packetEncoding", server.PacketEncoding, VMessGlobal.PacketEncodings);
        server.FakeType = Choice(request, "fakeType", server.FakeType, VMessGlobal.FakeTypes);
        server.Host = Optional(request, "host", server.Host);
        server.Path = Optional(request, "path", server.Path, 2048);
        server.QUICSecure = Choice(request, "quicSecure", server.QUICSecure ?? VMessGlobal.QUIC[0], VMessGlobal.QUIC);
        var mux = Optional(request, "useMux", server.UseMux?.ToString().ToLowerInvariant() ?? string.Empty);
        server.UseMux = mux switch { null or "" => null, "true" => true, "false" => false, _ => throw InvalidField("useMux") };
        server.TLSSecureType = Choice(request, "tlsSecureType", server.TLSSecureType, tlsOptions);
    }

    private static string UuidSecret(ServerDetailsWriteRequest request, string key, string? current, bool creating)
    {
        var value = Secret(request, key, current, creating, required: true);
        if (!Guid.TryParse(value, out _))
            throw new AppException("INVALID_SERVER_FIELD", "User ID must be a valid UUID.");
        return value;
    }

    private static string Secret(
        ServerDetailsWriteRequest request,
        string key,
        string? current,
        bool creating,
        bool required,
        bool multiline = false)
    {
        var clear = request.ClearSecrets?.Contains(key, StringComparer.Ordinal) == true;
        string? raw = null;
        var supplied = request.Secrets?.TryGetValue(key, out raw) == true && !string.IsNullOrEmpty(raw);
        var value = clear ? string.Empty : supplied ? NormalizeField(raw, key, multiline ? 131072 : 32768, multiline) : current ?? string.Empty;
        if (creating && !supplied)
            value = string.Empty;
        if (required && string.IsNullOrEmpty(value))
            throw new AppException("SERVER_SECRET_REQUIRED", $"A value is required for {key}.");
        return value;
    }

    private static string Required(ServerDetailsWriteRequest request, string key, string? current, int maxLength)
    {
        var value = Optional(request, key, current, maxLength);
        if (string.IsNullOrWhiteSpace(value))
            throw new AppException("INVALID_SERVER_FIELD", $"A value is required for {key}.");
        return value;
    }

    private static string? Optional(
        ServerDetailsWriteRequest request,
        string key,
        string? current,
        int maxLength = 4096,
        bool multiline = false)
    {
        if (request.Values?.TryGetValue(key, out var raw) != true)
            return current;
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return NormalizeField(raw, key, maxLength, multiline);
    }

    private static string Choice(ServerDetailsWriteRequest request, string key, string current, IReadOnlyList<string> choices)
    {
        var value = Optional(request, key, current) ?? string.Empty;
        if (!choices.Contains(value, StringComparer.Ordinal))
            throw InvalidField(key);
        return value;
    }

    private static int Integer(ServerDetailsWriteRequest request, string key, int current, int minimum, int maximum)
    {
        var value = Optional(request, key, current.ToString());
        if (!int.TryParse(value, out var result) || result < minimum || result > maximum)
            throw InvalidField(key);
        return result;
    }

    private static string NormalizeField(string? raw, string key, int maxLength, bool multiline)
    {
        var value = raw?.Trim() ?? string.Empty;
        var invalidControl = value.Any(character => char.IsControl(character) && (!multiline || character is not ('\r' or '\n' or '\t')));
        if (value.Length > maxLength || invalidControl)
            throw InvalidField(key);
        return value;
    }

    private static AppException InvalidField(string key) =>
        new("INVALID_SERVER_FIELD", $"The value for {key} is invalid.");

    private static bool HasSecret(string? value) => !string.IsNullOrEmpty(value);

    private static List<string> GetFavoriteKeys() => Global.Settings.FavoriteServers ??= new List<string>();

    private static void AddFavoriteKey(string key)
    {
        var favorites = GetFavoriteKeys();
        if (!favorites.Contains(key, StringComparer.Ordinal))
            favorites.Add(key);
    }

    private static void RestoreFavoriteKeys(IEnumerable<string> values)
    {
        var favorites = GetFavoriteKeys();
        favorites.Clear();
        favorites.AddRange(values);
    }
}
