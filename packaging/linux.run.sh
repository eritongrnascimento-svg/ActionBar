#!/bin/sh
set -eu
[ "$(uname -m)" = x86_64 ] || { echo 'Instalador para Linux x86-64.' >&2; exit 1; }
line=$(awk '/^__ACTIONBAR_PAYLOAD__$/{print NR + 1; exit}' "$0")
if [ "${1:-}" = --extract ] && [ "$#" -eq 2 ]; then
    mkdir -p -- "$2"
    tail -n +"$line" "$0" | tar -xz -C "$2"
    exit
fi
[ "$#" -eq 0 ] || { echo 'Uso: sh instalador.run [--extract DIRETÓRIO]' >&2; exit 2; }
[ "$(id -u)" -ne 0 ] || { echo 'Execute como usuário normal.' >&2; exit 1; }
python3 -c 'import sys; sys.exit(sys.version_info < (3, 10))' || { echo 'Python 3.10 ou superior é necessário.' >&2; exit 1; }
base=${XDG_DATA_HOME:-"$HOME/.local/share"}
bin="$HOME/.local/bin"
mkdir -p -- "$base" "$bin" "$base/applications" "$base/icons/hicolor/256x256/apps"
stage=$(mktemp -d "$base/.actionbar-install.XXXXXX")
trap 'rm -rf -- "$stage"' EXIT HUP INT TERM
tail -n +"$line" "$0" | tar -xz -C "$stage"
target="$base/actionbar-install-@VERSION@"
if [ ! -e "$target" ]; then
    mv -T -- "$stage/usr/lib/actionbar" "$target"
fi
install -m 755 "$stage/usr/bin/actionbar" "$target/actionbar"
if [ -e "$bin/actionbar" ] && ! grep -Fq '# Gerado pelo instalador ActionBar' "$bin/actionbar"; then
    echo "O comando $bin/actionbar já existe. O aplicativo foi extraído para $target." >&2
    exit 1
fi
env_tmp=$(mktemp "$bin/.actionbar.XXXXXX")
{
    printf '%s\n' '#!/bin/sh' '# Gerado pelo instalador ActionBar' 'set -eu'
    printf '%s\n' 'export ACTIONBAR_INSTALL_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/actionbar-install-@VERSION@"'
    printf '%s\n' 'exec "$ACTIONBAR_INSTALL_DIR/actionbar" "$@"'
} > "$env_tmp"
chmod 755 "$env_tmp"
mv -f -- "$env_tmp" "$bin/actionbar"
sed 's|^Exec=actionbar$|Exec=sh -c "exec \\"\\$HOME/.local/bin/actionbar\\""|' "$stage/usr/share/applications/actionbar.desktop" > "$base/applications/actionbar.desktop"
install -m 644 "$stage/usr/share/icons/hicolor/256x256/apps/actionbar.png" "$base/icons/hicolor/256x256/apps/actionbar.png"
printf 'ActionBar instalado. Abra pelo menu ou execute "%s".\n' "$bin/actionbar"
exit 0
__ACTIONBAR_PAYLOAD__

