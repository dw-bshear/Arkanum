#!/usr/bin/env bash
set -euo pipefail

CONCLAVE_PACKAGE_VERSION=${CONCLAVE_PACKAGE_VERSION:-2026.0.1}
ACTION=${ACTION:-auto}
INSTALL_DIR=${INSTALL_DIR:-/opt/conclave}
CONFIG_DIR=${CONFIG_DIR:-/etc/conclave}
DATA_DIR=${DATA_DIR:-/var/lib/conclave}
LOG_DIR=${LOG_DIR:-/var/log/conclave}
BACKUP_ROOT=${BACKUP_ROOT:-/var/backups/conclave}
SERVICE_USER=${SERVICE_USER:-conclave}
APP_URLS=${APP_URLS:-http://127.0.0.1:5068}
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PACKAGE_ROOT=$(cd "${SCRIPT_DIR}/../.." && pwd)
PACKAGE_DIR=${PACKAGE_DIR:-}
STATE_FILE="${CONFIG_DIR}/install-state.json"
REPORT_FILE="${LOG_DIR}/upgrade-report-${CONCLAVE_PACKAGE_VERSION}.txt"

fail() {
  echo "[FAIL] $*" >&2
  exit 1
}

info() {
  echo "[INFO] $*"
}

write_report() {
  install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 750 "${LOG_DIR}" 2>/dev/null || install -d -m 750 "${LOG_DIR}"
  {
    echo "Conclave installer report"
    echo "Product: Conclave"
    echo "Package version: ${CONCLAVE_PACKAGE_VERSION}"
    echo "Install action: ${ACTION}"
    echo "Install directory: ${INSTALL_DIR}"
    echo "Config directory: ${CONFIG_DIR}"
    echo "Data directory: ${DATA_DIR}"
    echo "Log directory: ${LOG_DIR}"
    echo "State file: ${STATE_FILE}"
    echo "Generated UTC: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "Health URL: ${APP_URLS}/health"
  } > "${REPORT_FILE}"
  chown "${SERVICE_USER}:${SERVICE_USER}" "${REPORT_FILE}" 2>/dev/null || true
  chmod 640 "${REPORT_FILE}" 2>/dev/null || true
}

if [ "${EUID}" -ne 0 ]; then
  fail "Run as root or with sudo."
fi

# Supported direct operator actions include ACTION=upgrade, ACTION=repair, and ACTION=restore.
case "${ACTION}" in
  auto|install|upgrade|repair|side-by-side|restore) ;;
  *) fail "ACTION must be one of auto, install, upgrade, repair, side-by-side, restore." ;;
esac
# Restore from database backup example: ACTION=restore CONCLAVE_RESTORE_KEY_FILE=/path/to/master-key.txt or CONCLAVE_RESTORE_KEY=ark-recovery-v1....

if [ -z "${PACKAGE_DIR}" ]; then
  if [ -f "${PACKAGE_ROOT}/app/Conclave.Web.dll" ]; then
    PACKAGE_DIR="${PACKAGE_ROOT}/app"
  elif [ -f "${PACKAGE_ROOT}/Conclave.Web.dll" ]; then
    PACKAGE_DIR="${PACKAGE_ROOT}"
  else
    PACKAGE_DIR="${PACKAGE_ROOT}/app"
  fi
fi

command -v dotnet >/dev/null 2>&1 || fail "dotnet runtime was not found. Install the supported ASP.NET Core runtime before running this installer."
command -v rsync >/dev/null 2>&1 || fail "rsync was not found. Install rsync before running this installer."
command -v python3 >/dev/null 2>&1 || fail "python3 was not found. Install python3 before running this installer."
command -v systemctl >/dev/null 2>&1 || fail "systemctl was not found. This installer expects a systemd-based Linux host."

if [ ! -f "${PACKAGE_DIR}/Conclave.Web.dll" ]; then
  fail "Published Conclave.Web.dll was not found in PACKAGE_DIR=${PACKAGE_DIR}. Run from an Conclave Linux package or set PACKAGE_DIR to the published app folder."
fi

if [ -z "${CONCLAVE_SQL_CONNECTION:-}" ]; then
  cat >&2 <<'EOF'
[FAIL] CONCLAVE_SQL_CONNECTION is required for Phase C installs and upgrades.

Example:
  sudo env \
    CONCLAVE_SQL_CONNECTION='Server=tcp:sql01,1433;Database=Conclave;User Id=conclave_app;Password=...;Encrypt=True;TrustServerCertificate=True;' \
    ACTION=restore \
    CONCLAVE_RESTORE_KEY_FILE=/path/to/master-key.txt \
    installers/linux/install-conclave.sh
EOF
  exit 4
fi

EXISTING_INSTALL=false
if [ -s "${STATE_FILE}" ] || [ -f "${INSTALL_DIR}/Conclave.Web.dll" ]; then
  EXISTING_INSTALL=true
fi

if [ "${ACTION}" = "auto" ]; then
  if [ "${EXISTING_INSTALL}" = true ]; then
    ACTION=upgrade
  else
    ACTION=install
  fi
fi

if [ "${ACTION}" = "side-by-side" ]; then
  INSTALL_DIR="${INSTALL_DIR}-${CONCLAVE_PACKAGE_VERSION}"
  CONFIG_DIR="${CONFIG_DIR}-${CONCLAVE_PACKAGE_VERSION}"
  DATA_DIR="${DATA_DIR}-${CONCLAVE_PACKAGE_VERSION}"
  LOG_DIR="${LOG_DIR}-${CONCLAVE_PACKAGE_VERSION}"
  STATE_FILE="${CONFIG_DIR}/install-state.json"
  REPORT_FILE="${LOG_DIR}/upgrade-report-${CONCLAVE_PACKAGE_VERSION}.txt"
  ACTION=install
fi

if [ "${EXISTING_INSTALL}" = false ] && [ "${ACTION}" = "upgrade" ]; then
  fail "ACTION=upgrade requested but no existing Conclave installation was detected. Use ACTION=install or ACTION=auto."
fi

if [ "${ACTION}" = "restore" ] && [ -z "${CONCLAVE_RESTORE_KEY_FILE:-}" ] && [ -z "${CONCLAVE_RESTORE_KEY:-}" ]; then
  fail "ACTION=restore requires CONCLAVE_RESTORE_KEY_FILE or CONCLAVE_RESTORE_KEY. Select Restore from database backup and provide the existing Conclave Recovery Key."
fi

if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
  info "Creating service user ${SERVICE_USER}."
  useradd --system --home "${DATA_DIR}" --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

info "Creating install/config/data/log directories."
install -d -o root -g root -m 755 "${INSTALL_DIR}"
install -d -o root -g root -m 755 "${CONFIG_DIR}"
install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 750 "${DATA_DIR}" "${LOG_DIR}"
install -d -o root -g root -m 750 "${BACKUP_ROOT}"

BACKUP_DIR=""
if [ "${ACTION}" = "upgrade" ] || [ "${ACTION}" = "repair" ]; then
  INSTALLED_VERSION="unknown"
  if [ -s "${STATE_FILE}" ]; then
    INSTALLED_VERSION=$(python3 - <<PY
import json
try:
    print(json.load(open('${STATE_FILE}')).get('installedVersion','unknown'))
except Exception:
    print('unknown')
PY
)
  fi
  BACKUP_DIR="${BACKUP_ROOT}/${INSTALLED_VERSION}-before-${CONCLAVE_PACKAGE_VERSION}-$(date -u +%Y%m%d%H%M%S)"
  info "Existing installation detected. Backing up app/config to ${BACKUP_DIR}."
  install -d -o root -g root -m 750 "${BACKUP_DIR}"
  [ -d "${INSTALL_DIR}" ] && rsync -a "${INSTALL_DIR}/" "${BACKUP_DIR}/app/" || true
  [ -d "${CONFIG_DIR}" ] && rsync -a --exclude 'master-key' "${CONFIG_DIR}/" "${BACKUP_DIR}/config/" || true
fi

info "Stopping conclave.service if it exists."
systemctl stop conclave.service >/dev/null 2>&1 || true

info "Copying published Conclave application from ${PACKAGE_DIR} to ${INSTALL_DIR}."
rsync -a --delete "${PACKAGE_DIR}/" "${INSTALL_DIR}/"
chown -R root:root "${INSTALL_DIR}"
chmod -R go-w "${INSTALL_DIR}"

MASTER_KEY_FILE="${CONFIG_DIR}/master-key"
if [ "${ACTION}" = "restore" ]; then
  info "Restore from database backup selected. Installing provided Conclave Recovery Key at ${MASTER_KEY_FILE}."
  umask 077
  if [ -n "${CONCLAVE_RESTORE_KEY_FILE:-}" ]; then
    [ -s "${CONCLAVE_RESTORE_KEY_FILE}" ] || fail "CONCLAVE_RESTORE_KEY_FILE does not exist or is empty."
    cp "${CONCLAVE_RESTORE_KEY_FILE}" "${MASTER_KEY_FILE}"
  else
    printf '%s\n' "${CONCLAVE_RESTORE_KEY}" > "${MASTER_KEY_FILE}"
  fi
  if ! grep -q '^ark-recovery-v1\.' "${MASTER_KEY_FILE}"; then
    fail "Provided Recovery Key must start with ark-recovery-v1."
  fi
elif [ ! -s "${MASTER_KEY_FILE}" ]; then
  info "Generating site-wide Conclave recovery/master key at ${MASTER_KEY_FILE}."
  umask 077
  python3 - <<'PY' > "${MASTER_KEY_FILE}"
import secrets
print('ark-recovery-v1.' + secrets.token_urlsafe(32))
PY
else
  info "Using existing site-wide Conclave recovery/master key at ${MASTER_KEY_FILE}."
fi
chown root:"${SERVICE_USER}" "${MASTER_KEY_FILE}"
chmod 640 "${MASTER_KEY_FILE}"

ENV_FILE="${CONFIG_DIR}/conclave.env"
info "Writing systemd environment file at ${ENV_FILE}."
cat > "${ENV_FILE}" <<EOF_ENV
ASPNETCORE_ENVIRONMENT=Production
CONCLAVE_MASTER_KEY=$(cat "${MASTER_KEY_FILE}")
ConnectionStrings__DefaultConnection=${CONCLAVE_SQL_CONNECTION}
EOF_ENV
chown root:"${SERVICE_USER}" "${ENV_FILE}"
chmod 640 "${ENV_FILE}"

DOTNET_BIN=$(command -v dotnet)
info "Writing systemd unit /etc/systemd/system/conclave.service."
cat > /etc/systemd/system/conclave.service <<EOF_SERVICE
[Unit]
Description=Conclave internal credential vault
After=network-online.target
Wants=network-online.target

[Service]
User=${SERVICE_USER}
Group=${SERVICE_USER}
WorkingDirectory=${INSTALL_DIR}
EnvironmentFile=${ENV_FILE}
ExecStart=${DOTNET_BIN} ${INSTALL_DIR}/Conclave.Web.dll --urls ${APP_URLS}
Restart=always
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF_SERVICE

info "Writing install state manifest at ${STATE_FILE}."
python3 - <<PY > "${STATE_FILE}"
import json, datetime
state = {
  "product": "Conclave",
  "installedVersion": "${CONCLAVE_PACKAGE_VERSION}",
  "installedAtUtc": datetime.datetime.utcnow().replace(microsecond=0).isoformat() + "Z",
  "installAction": "${ACTION}",
  "installPath": "${INSTALL_DIR}",
  "configPath": "${CONFIG_DIR}",
  "dataPath": "${DATA_DIR}",
  "logPath": "${LOG_DIR}",
  "serviceName": "conclave.service",
  "databaseProvider": "SqlServer",
  "packageChannel": "prerelease",
  "backupPath": "${BACKUP_DIR}"
}
print(json.dumps(state, indent=2))
PY
chown root:"${SERVICE_USER}" "${STATE_FILE}"
chmod 640 "${STATE_FILE}"

write_report

info "Enabling and restarting conclave.service."
systemctl daemon-reload
systemctl enable conclave.service
systemctl restart conclave.service

HEALTH_URL="${APP_URLS/0.0.0.0/127.0.0.1}/health"
info "Running post-install health check at ${HEALTH_URL}."
set +e
HEALTH_OUTPUT=$(python3 - <<PY 2>&1
import json, urllib.request, sys
url='${HEALTH_URL}'
try:
    with urllib.request.urlopen(url, timeout=20) as response:
        body=response.read().decode('utf-8', 'replace')
    print(body)
    data=json.loads(body)
    sys.exit(0 if str(data.get('status','')).lower() == 'healthy' else 2)
except Exception as exc:
    print(exc)
    sys.exit(3)
PY
)
HEALTH_STATUS=$?
set -e
{
  echo "Post-install health status code: ${HEALTH_STATUS}"
  echo "${HEALTH_OUTPUT}"
} >> "${REPORT_FILE}"

if [ "${HEALTH_STATUS}" -ne 0 ]; then
  if [ "${ACTION}" = "restore" ]; then
    echo "[FAIL] The provided Recovery Key does not match this database. Report: ${REPORT_FILE}" >&2
    exit 7
  fi
  cat >&2 <<EOF
[WARN] Conclave installed but health validation did not pass.
Report: ${REPORT_FILE}
Rollback app files from backup if needed:
  ${BACKUP_DIR}
Rollback app files command example:
  sudo rsync -a ${BACKUP_DIR}/app/ ${INSTALL_DIR}/ && sudo systemctl restart conclave.service
EOF
else
  info "Health validation passed."
fi

cat <<EOF
[OK] Conclave ${ACTION} completed for version ${CONCLAVE_PACKAGE_VERSION}.

Report:
  ${REPORT_FILE}

State manifest:
  ${STATE_FILE}

Next checks:
  sudo systemctl status conclave.service --no-pager
  APP_URL=${APP_URLS/http:\/\/0.0.0.0/http:\/\/127.0.0.1} installers/linux/verify-conclave.sh

Important recovery material:
  ${MASTER_KEY_FILE}

Back up the SQL database and this site-wide recovery/master key together. The database alone cannot decrypt existing vault contents after restore.
EOF
