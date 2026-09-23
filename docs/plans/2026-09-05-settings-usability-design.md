# Settings reliability and everyday connection UI

## Problem and approach

The settings document reaches App.svelte and becomes a deep `$state` proxy.
SettingsModal passes that proxy to `structuredClone`, which throws before the
draft is assigned. The user sees "Preparing settings" indefinitely.
Use `$state.snapshot` at the draft boundary and retain the existing typed IPC
and C# SettingsService. Request errors stay in a closable dialog with retry.
Edits are local until save; failed saves preserve the draft.

The previous UI uses many 8–11px labels and devotes half its workspace to logs.
Keep its dark/amber design but raise text to at least 13px, key controls to
14–16px, and provide a single connection column with a clear status heading.
Keep server navigation on the left and connection action at the bottom.
Show system VPN and app selection up front; keep all original modes and saved
profiles under Advanced routing. Logs remain live and open on demand.
Subscriptions get an explicit sidebar entry.

## Branding

Use the supplied SVG in WebUI. Generate a multi-resolution ICO from the PNG
for the existing application/window/tray resource. Do not redraw the artwork.
Keep a reproducible conversion script.

## Project organization

An all-at-once source move would affect native include/library paths and core
build scripts. For this iteration, organize editor roots by frontend, C# and
tests, document actual ownership, and exclude generated SDK/cache/build files.
Keep runtime data in release-local intact. Default new packages to artifacts/Netch.
Always publish the .NET/WebUI application before packaging so a pre-existing
output directory cannot silently keep an old executable. Native artifacts may
be explicitly reused for local packaging.

## Verification

Run a regression test through the real Svelte App parent, covering settings
sections, cancel/reopen, plain save payload, load retry, failed save and lock.
Run Svelte check, production build, .NET tests, Windows ICO decoding and verify
packaged asset hashes. DOM tests use a test-only mocked application API;
they do not establish WebView2 rendering, native IPC or live networking behavior.
