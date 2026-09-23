<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { ServerDetailsWrite, ServerEditor, ServerProtocol } from '../lib/types';

  type Field = { key: string; label: string; help: string; type?: 'text' | 'number' | 'select' | 'textarea' | 'secret' | 'secret-textarea'; option?: string; required?: boolean; min?: number; max?: number };

  let { server = null, protocols, options, groups, busy, onClose, onSave }: {
    server?: ServerEditor | null;
    protocols: ServerProtocol[];
    options: Record<string, string[]>;
    groups: string[];
    busy: boolean;
    onClose: () => void;
    onSave: (value: ServerDetailsWrite) => void;
  } = $props();

  let draft = $state<ServerDetailsWrite | null>(null);
  let configured = $state<Record<string, boolean>>({});
  let initialized = $state(false);
  let creating = $derived(!server);

  $effect(() => {
    if (initialized) return;
    if (server) {
      draft = {
        id: server.id, protocol: server.protocol, name: server.name,
        group: server.group === 'NONE' ? '' : server.group,
        hostname: server.hostname, port: server.port,
        values: { ...server.values }, secrets: {}, clearSecrets: []
      };
      configured = { ...server.secretConfigured };
    } else {
      draft = fresh(protocols[0] ?? 'SS');
      configured = {};
    }
    initialized = true;
  });

  function fresh(protocol: ServerProtocol): ServerDetailsWrite {
    const ports: Record<ServerProtocol, number> = { SS: 8388, SSR: 8388, SOCKS: 1080, Trojan: 443, VMess: 443, VLESS: 443, SSH: 22, WireGuard: 51820 };
    const values: Record<string, string | null> = {};
    if (protocol === 'SS') Object.assign(values, { encryptMethod: options['ss.encryptMethod']?.[4] ?? 'aes-128-gcm', plugin: '', pluginOption: '' });
    if (protocol === 'SSR') Object.assign(values, { encryptMethod: options['ssr.encryptMethod']?.[4] ?? 'aes-128-gcm', protocol: options['ssr.protocol']?.[0] ?? 'origin', protocolParam: '', obfs: options['ssr.obfs']?.[0] ?? 'plain', obfsParam: '' });
    if (protocol === 'SOCKS') Object.assign(values, { username: '', remoteHostname: '', version: options['socks.version']?.[0] ?? '5' });
    if (protocol === 'Trojan') Object.assign(values, { host: '', tlsSecureType: 'tls' });
    if (protocol === 'VMess' || protocol === 'VLESS') Object.assign(values, {
      serverName: '', alterId: '0', encryptMethod: protocol === 'VMess' ? 'auto' : 'none', transferProtocol: 'tcp',
      packetEncoding: 'xudp', fakeType: 'none', host: '', path: '', quicSecure: 'none', useMux: '',
      tlsSecureType: 'none', fingerprint: '', realityPublicKey: '', realityShortId: '', realitySpiderX: ''
    });
    if (protocol === 'SSH') Object.assign(values, { user: 'root', publicKey: '' });
    if (protocol === 'WireGuard') Object.assign(values, { localAddresses: '172.16.0.2', peerPublicKey: '', mtu: '1420' });
    return { protocol, name: '', group: '', hostname: '', port: ports[protocol], values, secrets: {}, clearSecrets: [] };
  }

  function changeProtocol(protocol: ServerProtocol) {
    if (!creating) return;
    draft = fresh(protocol);
    configured = {};
  }

  function fields(protocol: ServerProtocol): Field[] {
    switch (protocol) {
      case 'SS': return [
        select('encryptMethod', $t("Encryption"), $t("Cipher supported by the remote server."), 'ss.encryptMethod'),
        secret('password', $t("Password"), $t("Write-only; existing value is never returned."), true),
        text('plugin', 'Plugin', $t("Optional SIP003 plugin executable.")), text('pluginOption', $t("Plugin options"), $t("Options passed to the selected plugin."))
      ];
      case 'SSR': return [
        select('encryptMethod', $t("Encryption"), $t("Legacy ShadowsocksR cipher."), 'ssr.encryptMethod'), secret('password', $t("Password"), $t("Write-only server password."), true),
        select('protocol', $t("Protocol"), $t("ShadowsocksR authentication protocol."), 'ssr.protocol'), text('protocolParam', $t("Protocol parameter"), $t("Optional protocol parameter.")),
        select('obfs', 'Obfuscation', $t("Traffic obfuscation method."), 'ssr.obfs'), text('obfsParam', 'OBFS parameter', $t("Optional obfuscation host or parameter."))
      ];
      case 'SOCKS': return [
        select('version', 'SOCKS version', $t("Remote proxy protocol version."), 'socks.version'), text('username', $t("Username"), $t("Leave empty for unauthenticated proxy.")),
        secret('password', $t("Password"), $t("Write-only authentication password.")), text('remoteHostname', $t("Remote address"), $t("Optional destination-visible address for private proxy hosts."))
      ];
      case 'Trojan': return [secret('password', $t("Password"), $t("Write-only Trojan password."), true), text('host', 'TLS host', 'SNI or certificate host override.'), select('tlsSecureType', 'TLS security', $t("Transport security mode."), 'vless.tls')];
      case 'VMess': return [...v2Fields(false), number('alterId', $t("Alter ID"), $t("Legacy VMess alter ID."), 0, 2147483647), select('encryptMethod', $t("Encryption"), $t("VMess security method."), 'vmess.encryptMethod')];
      case 'VLESS': return [...v2Fields(true), text('encryptMethod', $t("Encryption"), $t("Normally none.")), select('fingerprint', 'TLS fingerprint', $t("uTLS client fingerprint for Reality/TLS."), 'vless.fingerprint'), text('realityPublicKey', $t("Reality public key"), $t("Remote Reality public key.")), text('realityShortId', $t("Reality short ID"), $t("Remote short identifier.")), text('realitySpiderX', $t("Reality spider X"), $t("Optional Reality spider path."))];
      case 'SSH': return [text('user', 'User', 'SSH login user.', true), secret('password', $t("Password"), $t("Supply a password or private key.")), { ...secret('privateKey', $t("Private key"), $t("Multiline private key; write-only.")), type: 'secret-textarea' }, { ...text('publicKey', $t("Host public key"), $t("Optional expected host public key.")), type: 'textarea' }];
      case 'WireGuard': return [text('localAddresses', $t("Local addresses"), $t("Tunnel addresses used by the local peer."), true), text('peerPublicKey', $t("Peer public key"), $t("Remote WireGuard public key."), true), secret('privateKey', $t("Private key"), $t("Local private key; write-only."), true), secret('preSharedKey', $t("Pre-shared key"), $t("Optional PSK; write-only.")), number('mtu', 'MTU', $t("Tunnel MTU from 576 to 9000."), 576, 9000)];
    }
  }

  function v2Fields(vless: boolean): Field[] {
    return [
      secret('userId', vless ? 'UUID' : $t("User ID"), $t("Write-only UUID credential."), true), text('serverName', $t("Server name (SNI)"), $t("TLS or Reality server name.")),
      select('transferProtocol', $t("Transport"), $t("V2Ray stream transport."), 'v2.transferProtocol'), select('packetEncoding', $t("Packet encoding"), $t("Packet transport compatibility."), 'v2.packetEncoding'),
      select('fakeType', $t("Header type"), $t("Transport header or camouflage."), 'v2.fakeType'), text('host', 'Host', $t("HTTP, WebSocket, or transport host.")),
      text('path', $t("Path / service"), $t("WebSocket path, HTTP path, or gRPC service.")), select('quicSecure', 'QUIC security', 'QUIC packet protection.', 'v2.quicSecure'),
      secret('quicSecret', 'QUIC secret', $t("Write-only QUIC key.")), select('useMux', 'Mux', $t("Inherit, enable, or disable multiplexing."), 'boolean.inherit'),
      select('tlsSecureType', $t("Transport security"), $t("TLS, XTLS, or Reality mode."), vless ? 'vless.tls' : 'vmess.tls')
    ];
  }

  function text(key: string, label: string, help: string, required = false): Field { return { key, label, help, required }; }
  function secret(key: string, label: string, help: string, required = false): Field { return { key, label, help, type: 'secret', required }; }
  function select(key: string, label: string, help: string, option: string): Field { return { key, label, help, type: 'select', option }; }
  function number(key: string, label: string, help: string, min: number, max: number): Field { return { key, label, help, type: 'number', min, max }; }

  function setValue(key: string, value: string) { if (draft) draft.values[key] = value; }
  function setSecret(key: string, value: string) { if (draft) draft.secrets[key] = value; }
  function setClear(key: string, clear: boolean) {
    if (!draft) return;
    draft.clearSecrets = clear ? [...new Set([...draft.clearSecrets, key])] : draft.clearSecrets.filter((value) => value !== key);
    if (clear) draft.secrets[key] = '';
  }
  function submit() {
    if (!draft || busy || !draft.name.trim() || !draft.hostname.trim() || draft.port < 1 || draft.port > 65535) return;
    onSave($state.snapshot(draft) as ServerDetailsWrite);
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  {#if draft}
    <div class="modal protocol-server-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="protocol-server-title">
      <header class="modal-header">
        <div><span class="eyebrow">{creating ? $t("Local endpoint") : $t("{0} endpoint", draft.protocol)}</span><h2 id="protocol-server-title">{creating ? $t("Add server") : $t("Edit server")}</h2><p>{$t("Protocol details are stored in the existing Netch configuration.")}</p></div>
        <button class="icon-button" aria-label={$t("Close editor")} onclick={onClose}><Icon name="x" size={17} /></button>
      </header>

      <form onsubmit={(event) => { event.preventDefault(); submit(); }}>
        <div class="protocol-server-body">
          <div class="import-note"><Icon name="lock" size={16} /><span><strong>{$t("Credentials are write-only")}</strong><small>{$t("Configured secrets are never returned to WebUI. Leave a secret blank to keep it unchanged.")}</small></span></div>
          <section class="server-editor-section">
            <div class="server-editor-section-head"><span>{$t("Identity")}</span><small>01</small></div>
            <div class="server-editor-grid common">
              <label class="protocol-field"><span><strong>{$t("Protocol")}</strong><small>{$t("Locked after creation.")}</small></span><select value={draft.protocol} disabled={!creating} onchange={(event) => changeProtocol(event.currentTarget.value as ServerProtocol)}>{#each protocols as protocol}<option value={protocol}>{protocol}</option>{/each}</select></label>
              <label class="protocol-field"><span><strong>{$t("Display name")}</strong><small>{$t("Visible in the server list.")}</small></span><input maxlength="128" bind:value={draft.name} required /></label>
              <label class="protocol-field"><span><strong>{$t("Local group")}</strong><small>{$t("Blank uses Local.")}</small></span><input maxlength="64" bind:value={draft.group} list="server-groups" /><datalist id="server-groups">{#each groups as group}<option value={group}></option>{/each}</datalist></label>
              <label class="protocol-field"><span><strong>{$t("Address")}</strong><small>{$t("Hostname or IP.")}</small></span><input maxlength="253" bind:value={draft.hostname} required spellcheck="false" /></label>
              <label class="protocol-field"><span><strong>{$t("Port")}</strong><small>1–65535.</small></span><input type="number" min="1" max="65535" bind:value={draft.port} required /></label>
            </div>
          </section>

          <section class="server-editor-section">
            <div class="server-editor-section-head"><span>{draft.protocol} {$t("parameters")}</span><small>02</small></div>
            <div class="server-editor-grid">
              {#each fields(draft.protocol) as field (field.key)}
                <label class:wide={field.type === 'textarea' || field.type === 'secret-textarea'} class:secret-field={field.type === 'secret' || field.type === 'secret-textarea'} class="protocol-field">
                  <span><strong>{$t(field.label)}{field.required ? ' *' : ''}</strong><small>{$t(field.help)}</small></span>
                  {#if field.type === 'select'}
                    <select value={draft.values[field.key] ?? ''} onchange={(event) => setValue(field.key, event.currentTarget.value)}>{#each options[field.option!] ?? [] as value}<option value={value}>{value || $t("Inherit global")}</option>{/each}</select>
                  {:else if field.type === 'number'}
                    <input type="number" min={field.min} max={field.max} value={draft.values[field.key] ?? ''} oninput={(event) => setValue(field.key, event.currentTarget.value)} />
                  {:else if field.type === 'secret' || field.type === 'secret-textarea'}
                    <div class="secret-input-row">
                      {#if field.type === 'secret-textarea'}<textarea rows="5" value={draft.secrets[field.key] ?? ''} disabled={draft.clearSecrets.includes(field.key)} placeholder={configured[field.key] ? $t("Configured — leave blank to keep") : $t("Paste private key")} oninput={(event) => setSecret(field.key, event.currentTarget.value)} spellcheck="false"></textarea>
                      {:else}<input type="password" value={draft.secrets[field.key] ?? ''} disabled={draft.clearSecrets.includes(field.key)} placeholder={configured[field.key] ? $t("Configured — leave blank to keep") : $t("Enter value")} oninput={(event) => setSecret(field.key, event.currentTarget.value)} autocomplete="new-password" />{/if}
                      {#if configured[field.key]}<span class="secret-state"><Icon name="check" size={11} />{$t("Configured")}</span><label class="secret-clear"><input type="checkbox" checked={draft.clearSecrets.includes(field.key)} onchange={(event) => setClear(field.key, event.currentTarget.checked)} />{$t("Clear")}</label>{/if}
                    </div>
                  {:else if field.type === 'textarea'}
                    <textarea rows="4" value={draft.values[field.key] ?? ''} oninput={(event) => setValue(field.key, event.currentTarget.value)} spellcheck="false"></textarea>
                  {:else}
                    <input value={draft.values[field.key] ?? ''} oninput={(event) => setValue(field.key, event.currentTarget.value)} spellcheck="false" />
                  {/if}
                </label>
              {/each}
            </div>
          </section>
        </div>

        <footer class="modal-footer server-form-footer">
          <span>{$t("* Required for new endpoints. Existing write-only values are preserved when blank.")}</span>
          <button type="button" class="button ghost" disabled={busy} onclick={onClose}>{$t("Cancel")}</button>
          <button class="button primary" disabled={busy || !draft.name.trim() || !draft.hostname.trim() || draft.port < 1 || draft.port > 65535}>{busy ? $t("Saving…") : creating ? $t("Add server") : $t("Save changes")}</button>
        </footer>
      </form>
    </div>
  {/if}
</div>
