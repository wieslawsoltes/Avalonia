#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts
dotnet run --project tests/Kanban.Core.Tests -c Release | tee artifacts/core.log
dotnet run --project tests/Kanban.Headless.Tests -c Release | tee artifacts/headless.log
dotnet build samples/Kanban.Sample.Desktop -c Release
dotnet publish samples/Kanban.Sample.Browser -c Release -o artifacts/browser | tee artifacts/browser-build.log
SITE=$(find artifacts/browser -name index.html -print | head -1)
SITE=$(dirname "$SITE")
python scripts/serve.py "$SITE" --port 8080 >artifacts/server.log 2>&1 &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true' EXIT
python tests/browser/validate.py --url http://127.0.0.1:8080 --output artifacts/validation
dotnet pack src/Kanban.Core -c Release -o artifacts/packages
dotnet pack src/Kanban.Avalonia -c Release -o artifacts/packages
