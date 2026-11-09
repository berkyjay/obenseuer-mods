#!/bin/bash
# Install or update the ledger site's service. Run on the server, as the user who will own it.
set -euo pipefail
cd "$(dirname "$0")/.."
sed -e "s|@USER@|$(id -un)|g" -e "s|@GROUP@|$(id -gn)|g" -e "s|@DIR@|$PWD|g" deploy/obenseuer-ledger.service |
    sudo tee /etc/systemd/system/obenseuer-ledger.service > /dev/null
sudo chmod 0644 /etc/systemd/system/obenseuer-ledger.service
sudo systemctl daemon-reload
sudo systemctl enable obenseuer-ledger.service
sudo systemctl restart obenseuer-ledger.service
systemctl --no-pager --lines=0 status obenseuer-ledger.service
