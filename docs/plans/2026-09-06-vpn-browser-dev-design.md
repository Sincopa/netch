# VPN reliability and browser development

Approved by the user on 2026-09-06.

- Select a dedicated whole-computer TUN profile by a stable role, never the first game profile. Keep advanced profiles available.
- Stop routing and DNS before the proxy. Drain and serialize process log output before disposing writers. Return DNS failure responses instead of passing nil responses into the native Go library.
- Keep application rules and the selected routing profile with each subscription. Use persistent subscription routing IDs and mode file paths; server selection and subscription renaming must not reset them. Preserve the old application selection when migrating.
- Start latency checks after bootstrap without blocking the interface.
- Reuse every Svelte screen through a development-only mock RPC transport. Include editable fixtures, simulated events and connection states, persisted demo settings, reset and error/empty scenarios. Production continues to require the desktop bridge.
- Remove country flag frames and duplicate import actions. Keep a clear server / traffic scope / connect flow, with technical settings under advanced controls.

Implementation order: runtime fixes; routing persistence and stable mode roles; UI and startup ping; mock transport and development controls; regression tests, browser verification and Windows packaging.

Validation: C# routing/profile and lifecycle tests, frontend type checking and interaction tests, Go DNS tests, browser interactions and a release package. System routing and a real VPN connection require a separate live Windows check; static tests do not establish VPN connectivity.
