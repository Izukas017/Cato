#!/bin/sh
# Desinstala Cato. Tus ajustes (~/.config/Cato) se conservan; bórralos a mano si quieres.
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
pkill -x Cato 2>/dev/null || true
rm -rf "$DATA/cato"
rm -f "$HOME/.local/bin/cato" \
      "$DATA/applications/io.github.Izukas017.Cato.desktop" \
      "$DATA/icons/hicolor/512x512/apps/io.github.Izukas017.Cato.png" \
      "${XDG_CONFIG_HOME:-$HOME/.config}/autostart/io.github.Izukas017.Cato.desktop"
echo "Cato desinstalado."
