# First-stage completion design

## Goal

Complete the user-visible gaps in the approved WebView2 vertical slice without replacing Netch networking code or changing the existing configuration format.

## Process routing

`ProcessService` remains the source of process data. It will collapse duplicate process instances by executable, expose all matching PIDs, read a friendly name and application icon when Windows permits it, and tolerate protected or short-lived processes. A whitelisted native file-picker action will return only a validated existing `.exe`; the frontend never supplies an arbitrary path to execute.

The process modal will support search, selected-only filtering, refresh, select all, clear all, and adding a non-running executable. Routing remains executable-name based because the existing Netch `Redirector.Handle` format is executable based. Selected names continue to be saved in the real custom Process Mode file.

## Subscription state

Subscription DTOs will include the current server count plus runtime refresh status, last successful refresh time, and the last user-safe error. Refresh operations will publish state before and after work so the modal updates without polling. Existing `SubscriptionUtil` and share-link parsers remain authoritative, including their plain-text/Base64 behavior.

Refresh metadata is session-scoped in this stage to avoid mutating the established `settings.json` schema. The UI explicitly renders an unknown/never state after restart instead of inventing timestamps.

## Connection telemetry

The backend will publish a structured traffic snapshot with received bytes, sent bytes where ETW provides them, formatted values, elapsed connection duration, and session start time. Updates remain throttled to one event per second. Connection state names will be mapped to the user-facing disconnected/connecting/connected/disconnecting/error states.

The current core does not expose cooperative cancellation during `MainController.StartAsync`; the UI will not present a fake instant-cancel action. Cancellation will be added only when the core startup stages can safely accept a token without racing `StopAsync`.

## Error handling and security

- Process enumeration failures are isolated per process.
- Icons are bounded PNG data URLs and are omitted on failure.
- File-picker results are canonicalized, checked for existence, and restricted to `.exe`.
- Subscription failures return structured errors and never expose raw stack traces or full subscription URLs.
- Connect/disconnect remains serialized by `ConnectionService`.

## Verification

- Run `svelte-check` and the production Vite build.
- Build the .NET 8 x64 Debug target.
- Add focused unit tests for DTO/state transformations that do not require drivers.
- Smoke-test WebView startup and IPC without starting TUN, proxy cores, or changing routes.
- Leave real subscription downloads and tunnel connection for an explicit manual integration test.

## Implementation order

1. Extend DTOs and application services.
2. Add the whitelisted executable picker and structured traffic events.
3. Update TypeScript contracts and bridge methods.
4. Refine process, subscription, and connection components.
5. Build and run non-invasive smoke checks.
