# Server navigation and tray design

## Scope

Complete the server-navigation requirements that remain after server CRUD: favorites, local country hints, sorting, a favorites filter, and server selection from the system tray. Existing search, group filtering, progressive latency tests, and list containment remain in place.

## Persistence and security

Favorites are owned by the C# application layer and saved in the existing settings document. A favorite is represented by a versioned SHA-256 fingerprint of the server protocol, normalized hostname, and port. The persisted value contains no credential, UUID, token, share link, or subscription URL. Old settings remain compatible because a missing favorites collection defaults to empty.

## Backend and IPC

`ServerIdentity` produces favorite fingerprints and local country-code hints. `CatalogService` enriches `ServerDto` with `isFavorite` and `countryCode`. `ServerService.SetFavoriteAsync` validates the server id, updates settings, publishes the refreshed catalog, and preserves a favorite when a local server endpoint is edited. The WebView bridge exposes only the typed `servers.setFavorite` method.

Country detection is synchronous and local: flag emoji and well-known location tokens in the server name/group are preferred, followed by recognized country-code hostname suffixes. Unknown countries remain unset and render the generic server mark; no remote GeoIP request blocks or leaks endpoint data.

## Desktop and frontend

The tray gains a server submenu containing the current server and favorites. Selecting an entry uses the same catalog selection path as WebUI and is disabled during connection transitions or while connected.

The compact sidebar gains a two-row toolbar, favorites-only toggle, sorting by source order/favorites/latency/name, country flags, and a favorite action in each context menu. The original order remains the default. Existing `content-visibility` optimization continues to cover large lists.

## Error handling and verification

Invalid/stale ids return structured application errors. UI errors use the existing toast path; tray errors use native notifications. Unit tests cover deterministic secret-free fingerprints and country inference. Verification includes Svelte checks/build, .NET tests, `git diff --check`, and a static IPC/security scan. No VPN/TUN connection or external GeoIP request is started.
