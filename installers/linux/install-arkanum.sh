#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR=${INSTALL_DIR:-/opt/arkanum}
CONFIG_DIR=${CONFIG_DIR:-/etc/arkanum}
DATA_DIR=${DATA_DIR:-/var/lib/arkanum}
LOG_DIR=${LOG_DIR:-/var/log/arkanum}
SERVICE_USER=${SERVICE_USER:-arkanum}
APP_URLS=${APP_URLS:-http://127.0.0.1:5068}
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PACKAGE_ROOT=$(cd "${SCRIPT_DIR}/../.." && pwd)
PACKAGE_DIR=${PACKAGE_DIR:-}

fail() {
  echo "[FAIL] $*" >&2
  exit 1
}

info() {
  echo "[INFO] $*"
}

if [ "${EUID}" -ne 0 ]; then
  fail "Run as root or with sudo."
fi

if [ -z "${PACKAGE_DIR}" ]; then
  if [ -f "${PACKAGE_ROOT}/app/Arkanum.Web.dll" ]; then
    PACKAGE_DIR="${PACKAGE_ROOT}/app"
  elif [ -f "${PACKAGE_ROOT}/Arkanum.Web.dll" ]; then
    PACKAGE_DIR="${PACKAGE_ROOT}"
  else
    PACKAGE_DIR="${PACKAGE_ROOT}/app"
  fi
fi

command -v dotnet >/dev/null 2>&1 || fail "dotnet runtime was not found. Install the supported ASP.NET Core runtime before running this installer."
command -v rsync >/dev/null 2>&1 || fail "rsync was not found. Install rsync before running this installer."
command -v python3 >/dev/null 2>&1 || fail "python3 was not found. Install python3 before running this installer."
command -v systemctl >/dev/null 2>&1 || fail "systemctl was not found. This installer expects a systemd-based Linux host."

if [ ! -f "${PACKAGE_DIR}/Arkanum.Web.dll" ]; then
  fail "Published Arkanum.Web.dll was not found in PACKAGE_DIR=${PACKAGE_DIR}. Run from an Arkanum Linux package or set PACKAGE_DIR to the published app folder."
fi

if [ -z "${ARKANUM_SQL_CONNECTION:-}" ]; then
  cat >&2 <<'EOF'
[FAIL] ARKANUM_SQL_CONNECTION is required for Phase 1 installs.

Example:
  sudo env \
    ARKANUM_SQL_CONNECTION='Server=tcp:sql01,1433;Database=Arkanum;User Id=arkanum_app;Password=...;Encrypt=True;TrustServerCertificate=True;' \
    installers/linux/install-arkanum.sh
EOF
  exit 4
fi

if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
  info "Creating service user ${SERVICE_USER}."
  useradd --system --home "${DATA_DIR}" --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

info "Creating install/config/data/log directories."
install -d -o root -g root -m 755 "${INSTALL_DIR}"
install -d -o root -g root -m 755 "${CONFIG_DIR}"
install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 750 "${DATA_DIR}" "${LOG_DIR}"

info "Copying published Arkanum application from ${PACKAGE_DIR} to ${INSTALL_DIR}."
rsync -a --delete "${PACKAGE_DIR}/" "${INSTALL_DIR}/"
chown -R root:root "${INSTALL_DIR}"
chmod -R go-w "${INSTALL_DIR}"

MASTER_KEY_FILE="${CONFIG_DIR}/master-key"
if [ ! -s "${MASTER_KEY_FILE}" ]; then
  info "Generating site-wide Arkanum recovery/master key at ${MASTER_KEY_FILE}."
  umask 077
  python3 - <<'PY' > "${MASTER_KEY_FILE}"
import secrets
print('ark-recovery-v1.' + secrets.token_urlsafe(32))
PY
else
  info "Using existing site-wide Arkanum recovery/master key at ${MASTER_KEY_FILE}."
fi
chown root:"${SERVICE_USER}" "${MASTER_KEY_FILE}"
chmod 640 "${MASTER_KEY_FILE}"

ENV_FILE="${CONFIG_DIR}/arkanum.env"
info "Writing systemd environment file at ${ENV_FILE}."
cat > "${ENV_FILE}" <<EOF_ENV
ASPNETCORE_ENVIRONMENT=Production
ARKANUM_MASTER_KEY=$(cat "${MASTER_KEY_FILE}")
ConnectionStrings__DefaultConnection=${ARKANUM_SQL_CONNECTION}
EOF_ENV
chown root:"${SERVICE_USER}" "${ENV_FILE}"
chmod 640 "${ENV_FILE}"

DOTNET_BIN=$(command -v dotnet)
info "Writing systemd unit /etc/systemd/system/arkanum.service."
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
ExecStart=${DOTNET_BIN} ${INSTALL_DIR}/Arkanum.Web.dll --urls ${APP_URLS}
Restart=always
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF_SERVICE

info "Enabling and restarting arkanum.service."
systemctl daemon-reload
systemctl enable arkanum.service
systemctl restart arkanum.service

cat <<EOF
[OK] Arkanum service installed.

Next checks:
  sudo systemctl status arkanum.service --no-pager
  APP_URL=${APP_URLS/http:\/\/0.0.0.0/http:\/\/127.0.0.1} installers/linux/verify-arkanum.sh

Important recovery material:
  ${MASTER_KEY_FILE}

Back up the SQL database and this site-wide recovery/master key together. The database alone cannot decrypt existing vault contents after restore.
EOF
