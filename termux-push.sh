#!/data/data/com.termux/files/usr/bin/bash
# Pemakaian:  bash termux-push.sh <username-github> <nama-repo> <token>
set -e
USER="$1"; REPO="$2"; TOKEN="$3"
if [ -z "$USER" ] || [ -z "$REPO" ] || [ -z "$TOKEN" ]; then
  echo "bash termux-push.sh <user> <repo> <token>"; exit 1
fi
pkg install -y git
git config --global user.name  "$USER"
git config --global user.email "$USER@users.noreply.github.com"
[ -d .git ] || git init -b main
git add -A
git commit -m "AssetStudioMod Android" || true
git remote remove origin 2>/dev/null || true
git remote add origin "https://$USER:$TOKEN@github.com/$USER/$REPO.git"
git push -u origin main
echo "Selesai. Buka tab Actions di github.com/$USER/$REPO untuk APK-nya."
