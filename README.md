<p align="center"><img src="src/Netch/Resources/Netch.png" width="128" /></p>

<div align="center">

# Netch
Proxy client with per-app (split) support, with VLESS support
</div>

## Why?
Netch, but with added REALITY support, and xray core updated to support new transports. Also with corrected per-proccess routing.

### Modes
- `ProcessMode` - Use Netfilter driver to intercept process traffic
- `ShareMode` - Share your network based on WinPcap / Npcap
- `TunMode` - Use WinTUN driver to create virtual adapter
- `WebMode` - Web proxy mode

### Protocols
- [`Socks5`](https://www.wikiwand.com/en/SOCKS)
- [`Shadowsocks`](https://shadowsocks.org)
- [`ShadowsocksR`](https://github.com/shadowsocksrr/shadowsocksr-libev)
- [`WireGuard`](https://www.wireguard.com)
- [`Trojan`](https://trojan-gfw.github.io/trojan)
- [`VMess`](https://www.v2fly.org)
- [`VLESS (With REALITY support)`](https://xtls.github.io)

### Others
- UDP NAT FullCone (Limited by your server)
- .NET 8 x64, Windows 10 1809+, WebView2 Runtime
- Svelte 5 / TypeScript / Vite (Node.js 22.13+ for development)

## Development

### Browser preview (no Windows service required)

```powershell
cd src/Netch.WebUI
npm ci
npm run dev
```

Open **http://127.0.0.1:5173/**. The real Svelte interface automatically uses demo
data in a regular browser. The **DEV** bar opens every dialog and switches between
connected, connecting, error, missing-driver and empty states. Imports, settings,
profiles, applications, diagnostics and updates are simulated. Demo changes are
saved in this browser; **Сбросить демо** restores the fixtures.

Edit `src/Netch.WebUI/src/lib/bridge/fixtures.ts` for sample content,
`src/Netch.WebUI/src/components` for screens and `src/Netch.WebUI/src/styles.css`
for styles. Vite updates the preview as you save. The demo transport is development-only;
production builds still require the Windows desktop bridge and cannot simulate a VPN.

Open `Netch.code-workspace` or `Netch.sln`. Application code lives in `src`,
tests in `tests`, external cores in `vendor`, and resources in `assets`.
See [project structure](docs/PROJECT_STRUCTURE.md) for the purpose of each directory.

```powershell
cd src/Netch.WebUI
npm ci
npm test
npm run build
cd ../..
dotnet test tests/Netch.Tests/Tests.csproj -c Release -p:Platform=x64 -p:SkipWebUIBuild=true
./build.ps1
```

The packaged app is in `artifacts/Netch`. Full native builds also require MSBuild/C++
and the toolchains used in `vendor`. Use `-SkipNativeBuild` only when the native
outputs have already been built. Production runs bundled WebUI files without a dev server.
To update branding from `assets/branding`, run `./scripts/update-icons.ps1`.

## License
Netch is licensed under the [GPLv3](https://raw.githubusercontent.com/netchx/netch/main/LICENSE) license
