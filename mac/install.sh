#!/bin/bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "$0")" && pwd)"
export PATH="/opt/homebrew/bin:/usr/local/bin:$HOME/.npm-global/bin:$HOME/.local/bin:$PATH"
variant=""
assistants=""

usage() {
    echo "Uso: bash mac/install.sh [--variante logos|mascotas|ambas] [--asistentes ambos|claude|codex]"
}
while [[ $# -gt 0 ]]; do
    case "$1" in
        --variante)
            [[ $# -ge 2 ]] || { usage >&2; exit 1; }
            variant="$2"; shift 2 ;;
        --asistentes)
            [[ $# -ge 2 ]] || { usage >&2; exit 1; }
            assistants="$2"; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) usage >&2; exit 1 ;;
    esac
done
if [[ "$(uname -s)" != Darwin ]]; then
    echo "Este instalador requiere macOS 13 o posterior." >&2; exit 1
fi
if ! /usr/bin/xcode-select -p >/dev/null 2>&1 || ! /usr/bin/xcrun --find swiftc >/dev/null 2>&1; then
    echo "Faltan las Command Line Tools. Ejecuta: xcode-select --install" >&2; exit 1
fi
if [[ -z "$variant" ]]; then
    read -r -p "Apariencia (con Ambas puedes cambiarla desde el widget): 1) Logotipos  2) Mascotas  3) Ambas [3]: " variant
    variant="${variant:-3}"
    case "$variant" in 1) variant=logos ;; 2) variant=mascotas ;; 3) variant=ambas ;; esac
fi
case "$variant" in
    logos|logotipos) variant_number=0; fixed=1 ;;
    mascotas) variant_number=1; fixed=1 ;;
    ambas) variant_number=1; fixed=0 ;;
    *) echo "Variante no válida: usa logos, mascotas o ambas." >&2; exit 1 ;;
esac
if [[ -z "$assistants" ]]; then
    read -r -p "Asistentes: 1) Ambos  2) Solo Claude  3) Solo Codex [1]: " assistants
    assistants="${assistants:-1}"
    case "$assistants" in 1) assistants=ambos ;; 2) assistants=claude ;; 3) assistants=codex ;; esac
fi
case "$assistants" in
    ambos|0) show=0; selected=(claude codex) ;;
    claude|1) show=1; selected=(claude) ;;
    codex|2) show=2; selected=(codex) ;;
    *) echo "Asistentes no válidos: usa ambos, claude o codex." >&2; exit 1 ;;
esac

find_cli() {
    local name="$1" directory candidate
    for directory in /opt/homebrew/bin /usr/local/bin "$HOME/.npm-global/bin" "$HOME/.local/bin"; do
        if [[ -x "$directory/$name" ]]; then printf '%s\n' "$directory/$name"; return 0; fi
    done
    candidate="$(/bin/zsh -lc "command -v $name" 2>/dev/null)" || return 1
    [[ "$candidate" == /* && -x "$candidate" ]] || return 1
    printf '%s\n' "$candidate"
}
for name in "${selected[@]}"; do
    if ! find_cli "$name" >/dev/null; then
        echo "No se encontró la CLI de $name."
        if command -v npm >/dev/null 2>&1; then
            answer=""
            if [[ -t 0 ]]; then read -r -p "¿Instalar $name con npm? [s/N]: " answer; fi
            case "$answer" in
                s|S|si|sí|SI|Sí)
                    package="@openai/codex"
                    [[ "$name" != claude ]] || package="@anthropic-ai/claude-code"
                    npm install -g "$package" ;;
            esac
        fi
        if ! find_cli "$name" >/dev/null; then
            echo "Instala la CLI de $name y vuelve a ejecutar este instalador." >&2
            exit 1
        fi
    fi
done

/bin/bash "$ROOT/build.sh"
BUILT="$ROOT/build/Usage Orbit.app"
APP="$HOME/Applications/Usage Orbit.app"
BIN="$APP/Contents/MacOS/Usage Orbit"
"$BUILT/Contents/MacOS/Usage Orbit" --startup off
# El ejecutable recién compilado puede cerrar también una instancia de otra ubicación.
"$BUILT/Contents/MacOS/Usage Orbit" --quit || {
    echo "No se pudo cerrar Usage Orbit. Ciérralo y vuelve a intentarlo." >&2; exit 1
}
mkdir -p "$HOME/Applications"
if [[ -e "$APP" ]]; then /bin/rm -rf -- "$APP"; fi
/usr/bin/ditto "$BUILT" "$APP"
"$BIN" --configure "$variant_number" "$show" "$fixed"
"$BIN" --startup on

for name in "${selected[@]}"; do
    executable="$(find_cli "$name")"
    export PATH="$(dirname -- "$executable"):$PATH"
    if [[ "$name" == claude ]]; then
        if ! "$executable" auth status 2>/dev/null | /usr/bin/grep -Eq '"loggedIn"[[:space:]]*:[[:space:]]*true'; then
            echo "Inicia sesión en Claude Code."
            "$executable" auth login || echo "Sesión pendiente: ejecuta claude auth login."
        fi
    else
        if ! "$executable" login status >/dev/null 2>&1; then
            echo "Inicia sesión en Codex."
            "$executable" login || echo "Sesión pendiente: ejecuta codex login."
        fi
    fi
done
/usr/bin/open "$APP"
echo "Usage Orbit está instalado. El inicio automático espera 3 minutos después de iniciar sesión."
