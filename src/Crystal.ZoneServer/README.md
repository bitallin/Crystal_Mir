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

## Configuration

Command line:

- `--content-root <path>` or `CRYSTAL_ZONE_HOME`
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

Ubuntu deployment and systemd installation are documented in
`docs/runbooks/zone-server-linux.md`.
