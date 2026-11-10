#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_DIR="/opt/crystal/zone"
DATA_DIR="/var/lib/crystal/zone"
ENV_FILE="/etc/crystal/zone.env"
UNIT_FILE="/etc/systemd/system/crystal-zone.service"
SERVICE_USER="crystal"

CONTENT_DIR="${CRYSTAL_ZONE_INSTALL_CONTENT:-}"
START_SERVICE=0
PUBLISH=1

usage() {
  cat <<'EOF'
Usage: sudo ./scripts/install-zone.sh [options]

Installs the self-contained Crystal Zone Server and its systemd unit.

Options:
  --content <dir>   Content directory to copy when DATA_DIR has no Server.MirDB
  --start           Enable and start the service after installation
  --no-publish      Use the existing artifacts/zone-install output
  -h, --help        Show this help

Environment:
  CRYSTAL_ZONE_INSTALL_CONTENT  Same as --content
EOF
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --content)
      [ "$#" -ge 2 ] || { echo "--content requires a directory" >&2; exit 2; }
      CONTENT_DIR="$2"
      shift 2
      ;;
    --start)
      START_SERVICE=1
      shift
      ;;
    --no-publish)
      PUBLISH=0
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

if [ "$(id -u)" -ne 0 ]; then
  echo "install-zone.sh must run as root." >&2
  exit 1
fi

if ! command -v systemctl >/dev/null 2>&1; then
  echo "systemctl is required. This installer targets Ubuntu with systemd." >&2
  exit 1
fi

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --home /var/lib/crystal --shell /usr/sbin/nologin "$SERVICE_USER"
fi

install -d -m 0755 "$APP_DIR"
install -d -o "$SERVICE_USER" -g "$SERVICE_USER" -m 0750 "$DATA_DIR"
install -d -m 0755 /etc/crystal

if [ "$PUBLISH" -eq 1 ]; then
  "$ROOT/scripts/publish-zone.sh" "$ROOT/artifacts/zone-install"
fi

if [ ! -x "$ROOT/artifacts/zone-install/Crystal.ZoneServer" ]; then
  echo "Published server not found: artifacts/zone-install/Crystal.ZoneServer" >&2
  exit 1
fi

find "$APP_DIR" -mindepth 1 -depth -delete
cp -a "$ROOT/artifacts/zone-install/." "$APP_DIR/"

if [ ! -f "$DATA_DIR/Server.MirDB" ]; then
  if [ -z "$CONTENT_DIR" ]; then
    echo "No Server.MirDB in $DATA_DIR; pass --content <dir> to install content." >&2
    exit 1
  fi

  for required in Server.MirDB Configs Envir Maps; do
    if [ ! -e "$CONTENT_DIR/$required" ]; then
      echo "Content directory is missing $required: $CONTENT_DIR" >&2
      exit 1
    fi
  done

  cp -a "$CONTENT_DIR/." "$DATA_DIR/"
else
  echo "Keeping existing content in $DATA_DIR."
fi

chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_DIR"

install -m 0644 "$ROOT/deploy/systemd/crystal-zone.service" "$UNIT_FILE"
if [ ! -f "$ENV_FILE" ]; then
  install -m 0640 -o root -g "$SERVICE_USER" "$ROOT/deploy/systemd/zone.env.example" "$ENV_FILE"
fi

systemctl daemon-reload
systemctl enable crystal-zone.service

if [ "$START_SERVICE" -eq 1 ]; then
  systemctl restart crystal-zone.service
fi

cat <<EOF
Installed Crystal Zone Server.

Application: $APP_DIR
Content:     $DATA_DIR
Environment: $ENV_FILE
Service:     crystal-zone.service

Next steps:
  sudo systemctl start crystal-zone
  sudo systemctl status crystal-zone
  sudo journalctl -u crystal-zone -f
EOF
