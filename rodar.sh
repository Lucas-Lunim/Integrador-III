#!/usr/bin/env bash
# Sobe o sistema para uso real, coletando da API industrial.
#
#   ./rodar.sh                  usa o ApiUrl do backend/Pi3.Backend/appsettings.json
#   ./rodar.sh 10.12.22.92      usa este IP (porta 8080)
#   ./rodar.sh http://IP:PORTA  usa este endereço
#
# Compila as telas se houver mudança no frontend e abre o backend em
# http://localhost:5080. Ctrl+C encerra.
set -euo pipefail
cd "$(dirname "$0")"

if [ $# -ge 1 ]; then
  case "$1" in
    *://*) export Pi3__ApiUrl="$1" ;;
    *:*)   export Pi3__ApiUrl="http://$1" ;;
    *)     export Pi3__ApiUrl="http://$1:8080" ;;
  esac
fi
api="${Pi3__ApiUrl:-$(grep -oP '"ApiUrl":\s*"\K[^"]+' backend/Pi3.Backend/appsettings.json)}"

for porta in 5080 8090; do
  if ss -ltn 2>/dev/null | grep -q ":$porta "; then
    echo "A porta $porta está em uso (backend ou simulador já rodando?). Encerre-o com Ctrl+C no terminal dele." >&2
    exit 1
  fi
done

echo "API industrial: $api"
if curl -s -m 4 -o /dev/null "$api/status"; then
  echo "A API respondeu."
else
  echo "Aviso: a API não respondeu. Confira o IP e se o notebook está na rede do laboratório."
  echo "O sistema sobe mesmo assim e tenta de novo a cada 5 s."
fi

indice=frontend/dist/painel/browser/index.html
if [ ! -d frontend/node_modules ]; then (cd frontend && npm install); fi
if [ ! -f "$indice" ] || [ -n "$(find frontend/src -newer "$indice" -print -quit)" ]; then
  echo "Compilando as telas..."
  (cd frontend && npm run build)
fi

echo "Abra http://localhost:5080"
cd backend
exec dotnet run --project Pi3.Backend -c Release --launch-profile http
