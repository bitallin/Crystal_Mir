# Crystal.ZoneServer

Linux-first console host for the legacy Crystal zone runtime. This project links
`Server.Library`, so it is inside the GPL-3.0-only boundary described in
`docs/adr/0004-gpl-boundary.md`.

## Run

```bash
dotnet run --project src/Crystal.ZoneServer -c Release -- \
  --content-root /path/to/content
```

The content directory must contain `Server.MirDB`, `Configs/`, `Envir/`, and
`Maps/`.

```bash
# Validate database and content references without opening network ports.
dotnet run --project src/Crystal.ZoneServer -c Release -- \
  --content-root /path/to/content --check
```

## Signed content release mode

`Crystal.ZoneServer` can consume an immutable, signed content release at startup:

```bash
dotnet run --project src/Crystal.ZoneServer -c Release -- \
  --content-root /path/to/content \
  --content-release /path/to/releases/2026.11.17.1 \
  --content-public-key /secure/content-public.pem \
  --content-key-id content-release-2026-01
```

In this mode the host verifies the manifest, entity hashes and ECDSA P-256 signature before loading anything.
It then rebuilds `Server.MirDB` in an isolated temporary directory, materializes `drop`, `recipe`, `script`
and `quest` content, and links or copies maps, configuration and other runtime files from `--content-root`.
The release directory and source content root are not modified.

Missing signatures, a mismatched key ID, a tampered snapshot, or a map/script/recipe materialization failure
stop startup and the host does not become ready. After a successful start, SIGTERM/SIGINT use the existing
save and graceful-shutdown path; the temporary workspace is cleaned up.

This is a startup-only read path. It does not implement Zone Agent, staging, hot reload, gray release or online
rollback; switching releases requires a restart. Without `--content-release`, the legacy binary database mode
remains the default and is unchanged.

## Configuration

Command line:

- `--content-root <path>` or `CRYSTAL_ZONE_HOME`
- `--content-release <path>` or `CRYSTAL_ZONE_CONTENT_RELEASE`
- `--content-public-key <path>` or `CRYSTAL_ZONE_CONTENT_PUBLIC_KEY`
- `--content-key-id <id>` or `CRYSTAL_ZONE_CONTENT_KEY_ID`
- `--startup-timeout <seconds>`
- `--log-config <path>`
- `--check`

Runtime environment:

- `CRYSTAL_ZONE_ID`
- `CRYSTAL_ZONE_IP`
- `CRYSTAL_ZONE_PORT`
- `CRYSTAL_ZONE_STATUS_PORT`
- `CRYSTAL_ZONE_MAX_USER`
- `CRYSTAL_ZONE_MAX_IP`
- `CRYSTAL_ZONE_THREADS`
- `CRYSTAL_ZONE_SAVE_DELAY_MINUTES`

Logs are written to the console and `Logs/zone.log` relative to the content
directory. SIGTERM and SIGINT trigger the legacy shutdown/save path.

`scripts/zone-smoke.sh` covers the legacy startup path, signed release startup, missing signature, tampered
snapshot and missing map failure paths.

Ubuntu deployment and systemd installation are documented in
`docs/runbooks/zone-server-linux.md`.
