# Settings and tray design

## Goal

Replace the legacy checkbox-heavy settings form with a typed, compact Svelte settings workspace while preserving the existing `Setting` model and `settings.json` compatibility.

## Boundary

The WebUI uses only `settings.get` and `settings.update`. `SettingsService` maps the existing model to explicit nested DTOs; there is no reflection-based property setter or arbitrary setting path. The update request is fully validated before any global value is changed.

Settings that affect networking are not applied while a connection is active or transitioning. The modal stays readable in that state but saving is disabled, and the backend enforces the same rule.

## Sections

- General: language and profile layout preferences.
- Connection: local proxy ports/address, latency method and intervals, request timeout, and STUN endpoint.
- Routing: existing Redirector protocol, DNS, parent-process, and ICMP options.
- Subscriptions: update-on-launch behavior and direct access to the subscription manager.
- DNS: current TUN and AioDNS values plus bypass IPs.
- Startup: Windows startup task, auto-connect, minimize, close-to-tray, and stop-on-exit behavior.
- Updates: startup and beta update checks.
- Advanced: existing Xray/V2Ray and KCP compatibility switches.

Each field has a short explanation. Advanced settings are visible but visually separated from everyday controls.

## Side effects

After validation, the service updates the existing model, synchronizes the Windows startup task through the existing utility, refreshes the latency-test timer, saves through `Configuration.SaveAsync`, and publishes `settings.changed`.

The native tray owns a checked `Close window to tray` item. It calls the same Settings service instead of editing global state directly, and updates immediately when the WebUI saves settings.

## Errors and recovery

- Ports must be 1-65535 and distinct where required.
- IP addresses, DNS values, STUN host/port, timeouts, intervals, and bypass entries are validated server-side.
- Startup task failures return a structured error and restore the previous startup preference.
- Raw exceptions and filesystem details are not returned to the frontend.

## Verification

- Add validation tests for accepted defaults and rejected ports/IP values.
- Run Svelte checks and production Vite build.
- Build the .NET 8 x64 target and run unit tests.
- Smoke-test WebView startup without connecting or changing routes.
