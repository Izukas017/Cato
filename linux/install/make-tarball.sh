#!/bin/sh
# Builds artifacts/Cato-<version>-linux-x64.tar.gz (self-contained, with install.sh). Run from the repo root.
set -e
VERSION=0.1.0
STAGE="artifacts/tar/cato-$VERSION"
rm -rf artifacts/tar && mkdir -p "$STAGE"
dotnet publish linux/Cato.Linux/Cato.Linux.csproj -c Release -r linux-x64 --self-contained true -p:DebugType=none -o "$STAGE/app"
cp linux/install/install.sh linux/install/uninstall.sh linux/flatpak/io.github.Izukas017.Cato.desktop "$STAGE/"
cp Assets/cato.png "$STAGE/cato.png"
chmod +x "$STAGE/install.sh" "$STAGE/uninstall.sh" "$STAGE/app/Cato"
tar -C artifacts/tar -czf "artifacts/Cato-$VERSION-linux-x64.tar.gz" "cato-$VERSION"
echo "artifacts/Cato-$VERSION-linux-x64.tar.gz"
