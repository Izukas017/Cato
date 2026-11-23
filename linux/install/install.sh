#!/bin/sh
# Instala Cato para tu usuario (sin sudo): ~/.local/share/cato, comando `cato` y entrada en el menú.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA/cato"

pkill -x Cato 2>/dev/null || true          # replace a running copy cleanly
rm -rf "$DEST"
mkdir -p "$DEST" "$HOME/.local/bin"
cp -r "$HERE/app/." "$DEST/"
chmod +x "$DEST/Cato"
ln -sf "$DEST/Cato" "$HOME/.local/bin/cato"

install -Dm644 "$HERE/cato.png" "$DATA/icons/hicolor/512x512/apps/io.github.Izukas017.Cato.png"
mkdir -p "$DATA/applications"
sed "s|^Exec=.*|Exec=$DEST/Cato|" "$HERE/io.github.Izukas017.Cato.desktop" > "$DATA/applications/io.github.Izukas017.Cato.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$DATA/applications" 2>/dev/null || true

echo "Cato instalado en $DEST"
echo "Ábrelo desde el menú de aplicaciones o con: cato"
case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *) echo "(Añade ~/.local/bin a tu PATH para usar el comando cato)";; esac
