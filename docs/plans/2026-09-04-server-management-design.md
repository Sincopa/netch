# Server management design

## Goal

Add safe WebUI management for all server formats already understood by Netch without exposing protocol credentials to the frontend or replacing existing parsers.

## Application boundary

`ServerService` owns text import, common-field updates, and deletion. Import delegates to `ShareLink.ParseText`, including its plain-text and Base64 subscription handling. Explicit DTOs expose only server identity, display metadata, host, port, protocol, latency, and subscription ownership.

All writes are rejected while a connection is active or transitioning because current Netch server IDs are list indexes. Updates and deletes validate the target, normalize user input, save through `Configuration.SaveAsync`, and restore the previous state if persistence fails.

Servers whose group matches a configured subscription are read-only. Local custom groups are allowed, but may not collide with subscription names. After mutations the service publishes `servers.changed`; the frontend also refreshes profiles so missing references remain visible.

## WebUI

The sidebar gets an import action and group filter. Each row exposes a compact custom action menu for latency testing, common-field editing, and deletion. Import uses a focused modal textarea; editing never receives or returns passwords, UUIDs, private keys, or other protocol-specific data.

Deletion uses a confirmation dialog. Subscription-owned rows direct the user to subscription management. Large lists use browser layout containment and `content-visibility` to avoid unnecessary rendering work.

## Verification

- Test input bounds, common-field normalization, subscription group collision, and selection-index adjustment.
- Run Svelte diagnostics and production Vite build.
- Build the .NET 8 x64 target and run all tests.
- Verify the RPC whitelist and scan DTOs for credential fields.
