#!/usr/bin/env bash
set -euo pipefail

VERSION=${VERSION:-2026.0.5}
ARCH=${ARCH:-amd64}
ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
APP_DIR=${APP_DIR:-${ROOT_DIR}/publish/arkanum-linux-x64}
OUT_DIR=${OUT_DIR:-${ROOT_DIR}/artifacts/native/linux}
STAGE_DIR="${OUT_DIR}/staging/arkanum-${VERSION}"
PACKAGE_NAME="Arkanum-${VERSION}-linux-x64.deb"

fail() { echo "[FAIL] $*" >&2; exit 1; }
command -v dpkg-deb >/dev/null 2>&1 || fail "dpkg-deb is required to build the DEB package."
[ -f "${APP_DIR}/Arkanum.Web.dll" ] || fail "APP_DIR must contain published Arkanum.Web.dll: ${APP_DIR}"

rm -rf "${STAGE_DIR}"
install -d "${STAGE_DIR}/DEBIAN" "${STAGE_DIR}/opt/arkanum" "${STAGE_DIR}/usr/share/doc/arkanum" "${STAGE_DIR}/usr/lib/arkanum/installers/linux"
rsync -a "${APP_DIR}/" "${STAGE_DIR}/opt/arkanum/"
rsync -a "${ROOT_DIR}/installers/linux/" "${STAGE_DIR}/usr/lib/arkanum/installers/linux/"
cp "${ROOT_DIR}/docs/upgrade-and-native-packaging.md" "${STAGE_DIR}/usr/share/doc/arkanum/"

cat > "${STAGE_DIR}/DEBIAN/control" <<EOF_CONTROL
Package: arkanum
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${ARCH}
Maintainer: Arkanum Release Engineering <name@email.com>
Depends: aspnetcore-runtime-9.0, python3, rsync, systemd
Description: Arkanum internal credential vault
 Arkanum is an internal credential vault with SQL Server storage and
 a site-wide recovery key model.
EOF_CONTROL

cat > "${STAGE_DIR}/DEBIAN/postinst" <<'EOF_POSTINST'
#!/usr/bin/env bash
set -e
cat <<'EOF'
Arkanum package files were staged under /opt/arkanum.
Run the GUI or script installer to configure SQL Server, service identity, recovery key, and systemd:
  python3 /usr/lib/arkanum/installers/linux/arkanum-gui-install.py
  sudo env ARKANUM_SQL_CONNECTION='Server=tcp:<host>,1433;Database=Arkanum;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;' /usr/lib/arkanum/installers/linux/install-arkanum.sh
EOF
EOF_POSTINST
chmod 755 "${STAGE_DIR}/DEBIAN/postinst"

install -d "${OUT_DIR}"
dpkg-deb --build "${STAGE_DIR}" "${OUT_DIR}/${PACKAGE_NAME}"
sha256sum "${OUT_DIR}/${PACKAGE_NAME}" > "${OUT_DIR}/${PACKAGE_NAME}.sha256"
echo "Built ${OUT_DIR}/${PACKAGE_NAME}"
