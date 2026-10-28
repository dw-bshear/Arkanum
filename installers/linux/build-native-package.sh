#!/usr/bin/env bash
set -euo pipefail

VERSION=${VERSION:-1.1.1}
ARCH=${ARCH:-amd64}
ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
APP_DIR=${APP_DIR:-${ROOT_DIR}/publish/arkanum-linux-x64}
OUT_DIR=${OUT_DIR:-${ROOT_DIR}/artifacts/native/linux}
STAGE_DIR="${OUT_DIR}/staging/arkanum-${VERSION}"
PACKAGE_NAME="Arkanum-${VERSION}-linux-x64.deb"

fail() { echo "[FAIL] $*" >&2; exit 1; }
command -v dpkg-deb >/dev/null 2>&1 || fail "dpkg-deb is required to build the DEB package."
NATIVE_VERSION=$(python3 "${ROOT_DIR}/installers/linux/arkanum_version.py" deb "${VERSION}") || fail "Unsupported native package version."
[ -f "${APP_DIR}/Arkanum.Web.dll" ] || fail "APP_DIR must contain published Arkanum.Web.dll: ${APP_DIR}"

rm -rf "${STAGE_DIR}"
install -d "${STAGE_DIR}/DEBIAN" "${STAGE_DIR}/usr/lib/arkanum/app" "${STAGE_DIR}/usr/share/doc/arkanum" "${STAGE_DIR}/usr/lib/arkanum/installers/linux"
rsync -a "${APP_DIR}/" "${STAGE_DIR}/usr/lib/arkanum/app/"
rsync -a --exclude '__pycache__' --exclude '*.pyc' "${ROOT_DIR}/installers/linux/" "${STAGE_DIR}/usr/lib/arkanum/installers/linux/"
rsync -a "${ROOT_DIR}/installers/sql/" "${STAGE_DIR}/usr/lib/arkanum/installers/sql/"
python3 - "${VERSION}" "${STAGE_DIR}/usr/lib/arkanum/package-manifest.json" <<'PYMANIFEST'
import json, sys
with open(sys.argv[2], 'w') as target:
    json.dump(dict(product='Arkanum', version=sys.argv[1], packageKind='full',
                   platform='linux-x64', requiresExistingInstall=False,
                   minimumInstalledVersion=None, preserves=[]), target, indent=2)
    target.write('\n')
PYMANIFEST
cp "${ROOT_DIR}/docs/upgrade-and-native-packaging.md" "${STAGE_DIR}/usr/share/doc/arkanum/"

cat > "${STAGE_DIR}/DEBIAN/control" <<EOF_CONTROL
Package: arkanum
Version: ${NATIVE_VERSION}
Section: utils
Priority: optional
Architecture: ${ARCH}
Maintainer: Arkanum Release Engineering <name@email.com>
Depends: aspnetcore-runtime-10.0, python3, rsync, systemd
Description: Arkanum internal credential vault
 Arkanum is an internal credential vault with SQL Server storage and
 a site-wide recovery key model.
EOF_CONTROL

cat > "${STAGE_DIR}/DEBIAN/preinst" <<'EOF_PREINST'
#!/usr/bin/env bash
set -e
# Older DEBs owned /opt/arkanum directly. dpkg would remove those live files
# when changing layout. Fail before unpack, not after losing rollback material.
if [ -n "${2:-}" ] && [[ "${2}" != 1:* ]]; then
  echo 'Unsupported legacy native DEB layout: use the full/update TAR installer and a separately planned native-package ownership migration. No files unpacked.' >&2
  exit 1
fi
EOF_PREINST
chmod 755 "${STAGE_DIR}/DEBIAN/preinst"

cat > "${STAGE_DIR}/DEBIAN/postinst" <<'EOF_POSTINST'
#!/usr/bin/env bash
set -e
cat <<'EOF'
Arkanum package files were staged under /usr/lib/arkanum/app (not the live installation).
Run the GUI or script installer to configure SQL Server, service identity, recovery key, and systemd:
  python3 /usr/lib/arkanum/installers/linux/arkanum-gui-install.py
  sudo env ARKANUM_SQL_CONNECTION='Server=tcp:<host>,1433;Database=Arkanum;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;' /usr/lib/arkanum/installers/linux/install-arkanum.sh
EOF
EOF_POSTINST
chmod 755 "${STAGE_DIR}/DEBIAN/postinst"

install -d "${OUT_DIR}"
dpkg-deb --build "${STAGE_DIR}" "${OUT_DIR}/${PACKAGE_NAME}"
(cd "${OUT_DIR}" && sha256sum "${PACKAGE_NAME}" > "${PACKAGE_NAME}.sha256")
echo "Built ${OUT_DIR}/${PACKAGE_NAME}"
