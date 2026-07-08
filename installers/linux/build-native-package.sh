#!/usr/bin/env bash
set -euo pipefail

VERSION=${VERSION:-2026.0.1}
ARCH=${ARCH:-amd64}
ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
APP_DIR=${APP_DIR:-${ROOT_DIR}/publish/conclave-linux-x64}
OUT_DIR=${OUT_DIR:-${ROOT_DIR}/artifacts/native/linux}
STAGE_DIR="${OUT_DIR}/staging/conclave-${VERSION}"
PACKAGE_NAME="Conclave-${VERSION}-linux-x64.deb"

fail() { echo "[FAIL] $*" >&2; exit 1; }
command -v dpkg-deb >/dev/null 2>&1 || fail "dpkg-deb is required to build the DEB package."
[ -f "${APP_DIR}/Conclave.Web.dll" ] || fail "APP_DIR must contain published Conclave.Web.dll: ${APP_DIR}"

rm -rf "${STAGE_DIR}"
install -d "${STAGE_DIR}/DEBIAN" "${STAGE_DIR}/opt/conclave" "${STAGE_DIR}/usr/share/doc/conclave" "${STAGE_DIR}/usr/lib/conclave/installers/linux"
rsync -a "${APP_DIR}/" "${STAGE_DIR}/opt/conclave/"
rsync -a "${ROOT_DIR}/installers/linux/" "${STAGE_DIR}/usr/lib/conclave/installers/linux/"
cp "${ROOT_DIR}/docs/upgrade-and-native-packaging.md" "${STAGE_DIR}/usr/share/doc/conclave/"

cat > "${STAGE_DIR}/DEBIAN/control" <<EOF_CONTROL
Package: conclave
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${ARCH}
Maintainer: Conclave Release Engineering <name@email.com>
Depends: aspnetcore-runtime-9.0, python3, rsync, systemd
Description: Conclave internal credential vault
 Conclave is an internal credential vault with SQL Server storage and
 a site-wide recovery key model.
EOF_CONTROL

cat > "${STAGE_DIR}/DEBIAN/postinst" <<'EOF_POSTINST'
#!/usr/bin/env bash
set -e
cat <<'EOF'
Conclave package files were staged under /opt/conclave.
Run the GUI or script installer to configure SQL Server, service identity, recovery key, and systemd:
  python3 /usr/lib/conclave/installers/linux/conclave-gui-install.py
  sudo env CONCLAVE_SQL_CONNECTION='Server=tcp:<host>,1433;Database=Conclave;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;' /usr/lib/conclave/installers/linux/install-conclave.sh
EOF
EOF_POSTINST
chmod 755 "${STAGE_DIR}/DEBIAN/postinst"

install -d "${OUT_DIR}"
dpkg-deb --build "${STAGE_DIR}" "${OUT_DIR}/${PACKAGE_NAME}"
sha256sum "${OUT_DIR}/${PACKAGE_NAME}" > "${OUT_DIR}/${PACKAGE_NAME}.sha256"
echo "Built ${OUT_DIR}/${PACKAGE_NAME}"
