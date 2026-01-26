<p align="center"><img src="https://github.com/NetchX/Netch/blob/main/Netch/Resources/Netch.png?raw=true" width="128" /></p>

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
- .NET 6.0 x64

## License
Netch is licensed under the [GPLv3](https://raw.githubusercontent.com/netchx/netch/main/LICENSE) license
