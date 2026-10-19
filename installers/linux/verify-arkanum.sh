#!/usr/bin/env bash
set -euo pipefail
APP_URL=${APP_URL:-https://127.0.0.1:5068}

if command -v systemctl >/dev/null 2>&1; then
  systemctl is-active --quiet arkanum.service && echo "[OK] arkanum.service is active" || { echo "[FAIL] arkanum.service is not active" >&2; exit 1; }
fi

health_json=$(curl -fsS "${APP_URL%/}/health")
python3 -c '
import json, sys
payload=json.load(sys.stdin)
print("[INFO] status={} version={}".format(payload.get("status"), payload.get("version")))
print("[INFO] liveness only; validate database/schema/key readiness independently.")
for check in payload.get("checks", []):
    status=check.get("status")
    name=check.get("name")
    message=check.get("message")
    print(f"[{status}] {name}: {message}")
if payload.get("status") not in ("Alive", "Healthy"):
    sys.exit(2)
' <<<"${health_json}"
