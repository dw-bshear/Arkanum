#!/usr/bin/env bash
set -euo pipefail
APP_URL=${APP_URL:-http://127.0.0.1:5068}

if command -v systemctl >/dev/null 2>&1; then
  systemctl is-active --quiet conclave.service && echo "[OK] conclave.service is active" || { echo "[FAIL] conclave.service is not active" >&2; exit 1; }
fi

health_json=$(curl -fsS "${APP_URL%/}/health")
python3 -c '
import json, sys
payload=json.load(sys.stdin)
print(f"[INFO] status={payload.get(\"status\")} version={payload.get(\"version\")}")
for check in payload.get("checks", []):
    status=check.get("status")
    name=check.get("name")
    message=check.get("message")
    print(f"[{status}] {name}: {message}")
if payload.get("status") != "Healthy":
    sys.exit(2)
' <<<"${health_json}"
