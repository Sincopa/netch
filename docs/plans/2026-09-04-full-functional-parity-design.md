# Full functional parity design

## Goal

Complete the approved WinForms-to-WebView2/Svelte migration while retaining the established Netch networking, controller, parser, mode-file, and settings implementations. The Svelte UI communicates only with typed, validated application services. Driver installation and update application always require an explicit user confirmation; verification never performs those actions.

## Delivery order

1. System foundation: diagnostics, capability reporting, driver actions, update discovery/download/verification/application, connection cancellation/reconnect, and startup update checks.
2. Advanced parity: create/update/delete Process and Route modes, expose TUN/Share mode details, validate mode rules, and retain the original mode-file format.
3. Server parity: protocol-aware create/update contracts for all server types already supported by Netch. Existing secrets are write-only from WebUI: the backend returns only presence flags, while an empty replacement retains the stored value.
4. UX completion: Diagnostics, Updates, and Advanced views; unavailable-mode explanations; confirmation gates; progress; reusable controls; keyboard navigation; tooltips; skeletons; and compact responsive behavior.
5. Hardening and retirement: compatibility tests, high-volume UI checks, IPC/security audit, real Windows/WebView2 smoke testing when explicitly authorized, then removal of WinForms surfaces whose functionality has reached parity.

## Architecture

`DiagnosticsService` reports immutable environment and component status. `DriverService` exposes a closed driver enum and delegates to the existing controller routines. `UpdateService` adapts the existing GitHub release parser and updater, owns state/progress, validates the release asset name and SHA-256, and requests host-controlled restart only after apply succeeds. `ModeManagementService` owns mode-file mutations and connection-state locking. Protocol editor adapters own mappings between typed public fields and existing server models so the application layer does not become one switch-heavy god service.

Long operations publish push events. Every mutation has one in-flight gate, cancellation where the underlying operation supports it, structured error codes, parameter limits, and rollback before publishing refreshed state.

## Security

The bridge remains an explicit whitelist; no shell command, arbitrary executable, URL opener, service name, or filesystem destination crosses IPC. The host owns UAC/restart/file-picker behavior. Subscription URLs and server credentials are excluded from logs and diagnostics. Update downloads are restricted to the selected release asset over HTTPS, stored under Netch data, checked against the release SHA-256, and never applied without confirmation.

## Connection lifecycle

Connection state expands to `reconnecting`. A dedicated connection-attempt cancellation source lets the user cancel while startup is in progress. Reconnect performs a serialized stop/start using the backend selection as source of truth. Connect, disconnect, reconnect, catalog mutations, mode mutations, update application, suspend/resume, and shutdown cannot race.

## UI direction

Keep the compact industrial dark shell and amber signal color. Diagnostics is a dense status matrix rather than oversized cards. Update progress is visible in the titlebar and a focused modal. Advanced mode/server editors use split navigation and restrained disclosure. Driver and destructive operations use explicit confirmation dialogs. The default home view continues to hide protocol and routing complexity.

## Verification

Each phase adds unit tests for validation, mapping, state transitions, secret redaction, compatibility, and rollback helpers. Svelte type checks/builds and .NET tests run after every phase. Static verification is not reported as proof of WebView2 rendering, driver installation, update replacement, or live VPN/TUN behavior.
