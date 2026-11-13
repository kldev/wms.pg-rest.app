#!/usr/bin/env bash
# Local docker compose stack: PostgreSQL + PostgREST + Blazor.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if docker compose version >/dev/null 2>&1; then
    COMPOSE=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
    COMPOSE=(docker-compose)
else
    echo "Docker Compose not found." >&2; exit 1
fi

if [ -f .env ]; then
    set -a
    # shellcheck disable=SC1091
    source .env
    set +a
fi

print_endpoints() {
    echo "Endpoints:"
    echo "  Web:        http://127.0.0.1:${WEB_PORT:-6500}            (health: /health)"
    echo "  PostgREST:  http://127.0.0.1:${API_PORT:-6501}            (OpenAPI: /)"
    echo "  PostgreSQL: 127.0.0.1:${DB_PORT:-6502}  db wms, user postgres (password in .env)"
}

usage() {
    cat <<USAGE
Usage: ./start.sh [OPTIONS]

Options:
  --build, -b             Rebuild images before start
  --foreground, -f        Run in foreground (logs in terminal)
  --logs, -l [svc]        Follow logs (default: web)
  --status, --ps          Show containers
  --config                Validate the compose file and .env
  --stop-service, -s svc  Stop one service (e.g. web, before 'dotnet run')
  --restart, -r svc       Rebuild and recreate one service
  --init-env              Create .env from .env-sample with random secrets
  --stop                  Stop and remove containers (data is kept)
  --clean, -c             Remove containers AND volumes (all data)
  --help, -h              Show this help
USAGE
}

require_env() {
    if [ ! -f .env ]; then
        echo "Missing .env - run: ./start.sh --init-env  (or copy .env-sample)" >&2
        exit 1
    fi
}

random_hex() { od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'; }

BUILD_ARG=()
DETACH_ARG=(-d --wait)

while [[ $# -gt 0 ]]; do
    case $1 in
        -b|--build) BUILD_ARG=(--build); shift ;;
        -f|--foreground) DETACH_ARG=(); shift ;;
        -l|--logs)
            service="web"
            if [[ -n "${2:-}" && "$2" != -* ]]; then service="$2"; fi
            "${COMPOSE[@]}" logs -f "$service"; exit 0 ;;
        --ps|--status) "${COMPOSE[@]}" ps -a; exit 0 ;;
        --config) "${COMPOSE[@]}" config -q && echo "Compose file OK"; exit 0 ;;
        -s|--stop-service)
            [[ -n "${2:-}" ]] || { echo "--stop-service needs a service name" >&2; exit 1; }
            "${COMPOSE[@]}" stop "$2"; exit 0 ;;
        -r|--restart)
            [[ -n "${2:-}" ]] || { echo "--restart needs a service name" >&2; exit 1; }
            require_env
            "${COMPOSE[@]}" up -d --build --no-deps --force-recreate "$2"; exit 0 ;;
        --init-env)
            if [ -f .env ]; then echo ".env already exists - not overwriting"; exit 0; fi
            sed -e "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(random_hex 16)/" \
                -e "s/^AUTHENTICATOR_PASSWORD=.*/AUTHENTICATOR_PASSWORD=$(random_hex 16)/" \
                -e "s/^JWT_SECRET=.*/JWT_SECRET=$(random_hex 32)/" \
                .env-sample > .env
            echo ".env created with random secrets"; exit 0 ;;
        --stop) "${COMPOSE[@]}" down; exit 0 ;;
        -c|--clean)
            echo "Removing containers and volumes..."
            "${COMPOSE[@]}" down -v --remove-orphans
            echo "Clean complete."; exit 0 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1"; echo ""; usage; exit 1 ;;
    esac
done

require_env

echo "Starting wms.pg-rest.app..."
"${COMPOSE[@]}" up "${BUILD_ARG[@]}" "${DETACH_ARG[@]}"

if [ ${#DETACH_ARG[@]} -gt 0 ]; then
    echo ""
    "${COMPOSE[@]}" ps --format 'table {{.Service}}\t{{.Status}}'
    echo ""
    print_endpoints
fi
