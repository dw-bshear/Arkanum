#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR=${INSTALL_DIR:-/opt/arkanum}
CONFIG_DIR=${CONFIG_DIR:-/etc/arkanum}
DATA_DIR=${DATA_DIR:-/var/lib/arkanum}
LOG_DIR=${LOG_DIR:-/var/log/arkanum}
SERVICE_USER=${SERVICE_USER:-arkanum}
APP_URLS=${APP_URLS:-http://127.0.0.1:5068}
PACKAGE_DIR=${PACKAGE_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}

if [ "${EUID}" -ne 0 ]; then
  echo "Run as root or with sudo." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet runtime was not found. Install the supported ASP.NET Core runtime before running this installer." >&2
  exit 2
fi

if [ ! -f "${PACKAGE_DIR}/Arkanum.Web.dll" ]; then
  echo "Published Arkanum.Web.dll was not found in PACKAGE_DIR=${PACKAGE_DIR}. Run from a published release package or set PACKAGE_DIR." >&2
  exit 3
fi

if [ -z "${ARKANUM_SQL_CONNECTION:-}" ]; then
  echo "ARKANUM_SQL_CONNECTION is required for Phase 1 installs." >&2
  echo "Example: Server=tcp:sql01,1433;Database=Arkanum;User Id=arkanum_app;Password=...;Encrypt=True;TrustServerCertificate=True;" >&2
  exit 4
fi

install -d -o root -g root -m 755 "${INSTALL_DIR}"
install -d -o root -g root -m 755 "${CONFIG_DIR}"
install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 750 "${DATA_DIR}" "${LOG_DIR}" 2>/dev/null || true

if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
  useradd --system --home "${DATA_DIR}" --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

rsync -a --delete --exclude installers --exclude docs "${PACKAGE_DIR}/" "${INSTALL_DIR}/"
chown -R root:root "${INSTALL_DIR}"
chmod -R go-w "${INSTALL_DIR}"

MASTER_KEY_FILE="${CONFIG_DIR}/master-key"
if [ ! -s "${MASTER_KEY_FILE}" ]; then
  umask 077
  python3 - <<'PY' > "${MASTER_KEY_FILE}"
import secrets
print(secrets.token_urlsafe(48))
PY
fi
chown root:"${SERVICE_USER}" "${MASTER_KEY_FILE}"
chmod 640 "${MASTER_KEY_FILE}"

ENV_FILE="${CONFIG_DIR}/arkanum.env"
cat > "${ENV_FILE}" <<EOF_ENV
ASPNETCORE_ENVIRONMENT=Production
ARKANUM_MASTER_KEY=$(cat "${MASTER_KEY_FILE}")
ConnectionStrings__DefaultConnection=${ARKANUM_SQL_CONNECTION}
EOF_ENV
chown root:"${SERVICE_USER}" "${ENV_FILE}"
chmod 640 "${ENV_FILE}"

cat > /etc/systemd/system/arkanum.service <<EOF_SERVICE
[Unit]
Description=Arkanum internal credential vault
After=network-online.target
Wants=network-online.target

[Service]
User=${SERVICE_USER}
Group=${SERVICE_USER}
WorkingDirectory=${INSTALL_DIR}
EnvironmentFile=${ENV_FILE}
ExecStart=$(command -v dotnet) ${INSTALL_DIR}/Arkanum.Web.dll --urls ${APP_URLS}
Restart=always
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF_SERVICE

systemctl daemon-reload
systemctl enable arkanum.service
systemctl restart arkanum.service

echo "Arkanum service installed. Run installers/linux/verify-arkanum.sh to validate health."
