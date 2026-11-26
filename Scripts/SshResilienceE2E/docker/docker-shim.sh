#!/bin/sh
# «docker compose» simulado: pull tarda poco, up tarda UP_SECONDS (por defecto 25) y cuenta cuántas veces se ejecutó.
verb=""
for a in "$@"; do case "$a" in pull|up|ps) verb=$a ;; esac; done
case "$verb" in
  pull) echo "pulling images"; sleep 2; echo "pull done" ;;
  up)
    n=$(cat /tmp/up-count 2>/dev/null || echo 0); echo $((n + 1)) > /tmp/up-count
    secs=$(cat /tmp/up-seconds 2>/dev/null || echo 25)
    echo "up start"
    i=1; while [ "$i" -le "$secs" ]; do echo "up step $i"; sleep 1; i=$((i + 1)); done
    echo "up done" ;;
  ps) echo "app running" ;;
  *) echo "docker shim: $*" ;;
esac
