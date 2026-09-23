# Quick profiles design

## Goal

Expose the existing Netch quick-profile model in the WebUI without changing the persisted `settings.json` format. A profile remains a numbered slot containing a name, server remark, and mode remark.

## Application boundary

`ProfileService` owns profile listing, saving, deletion, and activation. The WebUI uses explicit `profiles.list`, `profiles.save`, `profiles.delete`, and `profiles.activate` RPC methods with typed DTOs. Slot indexes, names, server IDs, and mode IDs are validated on the C# side.

Activation resolves both references before changing the current selection, saves the selected indexes once, and publishes `selection.changed`. It does not start or restart a connection. Missing references are returned as a `missing` profile state and existing data is preserved.

## WebUI

The connection workspace receives a compact quick-profile strip. Ready slots can be activated with one click; empty slots open the manager. The manager lists every configured slot and supports saving the current server/mode, renaming, overwriting, and deleting with confirmation.

Profiles refresh through `profiles.changed` and when server, mode, or profile-count changes can invalidate their resolved references. Process selections remain part of the selected Process Mode, matching the legacy model.

## Compatibility and errors

No migration or new profile file is introduced. Existing profiles with blank names remain readable, while newly saved names are normalized. Missing slots and references return structured errors. Raw filesystem or configuration exceptions are not exposed to the frontend.

## Verification

- Unit-test slot/name validation and reference resolution.
- Run Svelte checks and production build.
- Build the .NET 8 x64 target and run all tests.
- Check RPC whitelist and configuration persistence paths.
