# WebView2 and Svelte migration design

## Goal

Replace the WinForms presentation layer incrementally while preserving Netch's existing server implementations, controllers, drivers, routing behavior, and `settings.json` format.

## Chosen approach

The first migration stage stays inside the existing `Netch` executable. A minimal borderless WinForms host owns WebView2, the tray icon, window lifecycle, single-instance integration, and native Windows actions. Svelte owns presentation only. Existing WinForms screens remain in the tree until equivalent WebUI flows are proven.

The application targets .NET 8 LTS. The production Svelte build is copied beside the executable and exposed to WebView2 through a virtual HTTPS host; no localhost server is required outside development.

## Boundaries

The frontend communicates only through a typed request/event bridge:

```text
Svelte stores and views
  -> typed TypeScript API
  -> JSON request/event envelope
  -> whitelisted WebView2 dispatcher
  -> application services
  -> existing Netch models, controllers, and utilities
```

The bridge does not accept shell commands, executable paths, or arbitrary reflection targets. Backend parameters are validated and failures are returned as structured errors without stack traces.

## First vertical slice

- Load real servers and modes from the existing configuration and mode directory.
- Preserve and update the existing selected server/mode indices.
- Connect and disconnect through the existing `MainController`.
- Publish authoritative connection state and status changes to the frontend.
- List, add, update, delete, and refresh subscriptions through the existing parser and configuration.
- Enumerate real Windows processes and persist selected process names through process-mode files.
- Stream Serilog events to the WebUI while retaining file and console logging.
- Keep tray open/connect/disconnect/update/exit actions native.
- Render a compact dark desktop UI with a server rail, mode/routing controls, realtime status/logs, and an amber primary action.

## Refactoring strategy

Application services own orchestration that currently lives in `MainForm`. Existing low-level controllers remain intact. UI callbacks from `MainController`, `ModeService`, subscriptions, and bandwidth are replaced incrementally with an application event hub. Compatibility shims may remain for WinForms-only screens not yet migrated, but new services cannot depend on `MainForm`.

Connection operations are serialized, cancellation-aware, and expose explicit stopped, starting, connected, stopping, and failed states. Selection mutations are rejected while a connection transition is active.

## Security and production behavior

- Only the virtual application origin may invoke IPC.
- DevTools, browser context menus, downloads, new windows, and external navigation are disabled in release builds.
- Subscription URLs are validated as HTTP or HTTPS and are redacted from logs.
- Process selection accepts normalized process names, never arbitrary commands.
- Production assets are local and startup does not depend on Vite.

## Verification

- Build Svelte with TypeScript checking enabled.
- Build the .NET solution on Windows with the .NET 8 SDK.
- Test JSON envelope parsing, method whitelist behavior, validation, and connection state transitions.
- Smoke-test window controls, tray, persisted selection, subscriptions, process selection, connect/disconnect, and pushed logs.
- Do not start a real tunnel, driver, or proxy during automated verification.

