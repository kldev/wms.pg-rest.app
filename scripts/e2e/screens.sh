#!/usr/bin/env bash
# Screenshots into docs/screens/ from the running local stack (./infrastructure/start.sh with DEMO_DATA=true).
# Needs Node, Google Chrome and a node_modules with Playwright: PLAYWRIGHT_NODE_MODULES=/path/to/node_modules
# (e.g. after `npm install playwright` in any folder). Credentials come from infrastructure/.env.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../.." && pwd)"
: "${PLAYWRIGHT_NODE_MODULES:?a node_modules folder with Playwright}"
set -a
# shellcheck disable=SC1091
source "$repo/infrastructure/.env"
set +a
mkdir -p "$repo/docs/screens"
NODE_PATH="$PLAYWRIGHT_NODE_MODULES" node "$repo/scripts/e2e/screens.cjs" "$repo/docs/screens"
