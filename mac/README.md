# Usage Orbit para macOS (experimental)

Versión AppKit para macOS 13 o posterior, sin dependencias externas ni proyecto de Xcode. **Se compila automáticamente en macOS con GitHub Actions, pero todavía no se ha probado su funcionamiento en un Mac.** Los scripts se ejecutan con Bash; no necesitan tener permiso de ejecución.

Desde la raíz del repositorio:

```sh
xcode-select --install # Solo si faltan las Command Line Tools.
bash mac/install.sh
```

Para elegir sin las preguntas iniciales:

```sh
bash mac/install.sh --variante mascotas --asistentes ambos
```

`--variante` acepta `logos`, `mascotas` o `ambas` (solo con `ambas` se puede cambiar la apariencia desde el widget); `--asistentes`, `ambos`, `claude` o `codex`. La vista se cambia desde el widget. Si falta una CLI seleccionada, el instalador ofrece instalarla con npm. El inicio de sesión de cada CLI puede requerir interacción. No se consultan ni se vigilan los asistentes desactivados.

Compilar y abrir sin instalar:

```sh
bash mac/build.sh
open 'mac/build/Usage Orbit.app'
```

El resultado usa la arquitectura del equipo que compila y una firma local ad hoc. No está notarizado. El inicio automático apunta a `~/Applications/Usage Orbit.app`, por lo que requiere instalar primero.

El menú permite cambiar apariencia, vista, asistentes e inicio automático, y muestra «Acerca de Usage Orbit». Las flechas ocultan el panel en un borde; clic en la pestaña lo restaura y arrastrarla cambia su altura. Cerrar termina la app y sus consultas auxiliares. Abrirla de nuevo restaura la instancia existente.

Los ajustes se guardan en `~/Library/Application Support/UsageOrbit/settings.json`: `X`, `Y`, `Variant`, `View`, `Show`, `DockSide`, `DockY`. Las posiciones usan puntos de AppKit, con origen abajo a la izquierda. Reinstalar conserva la posición, la vista y la ocultación. El LaunchAgent `~/Library/LaunchAgents/com.usageorbit.widget.plist` abre la app 180 segundos después del inicio de sesión.

Claude usa una fuente OAuth no documentada, aislada en `Sources/ClaudeSource.swift`. Puede aparecer una solicitud de acceso al llavero de macOS. Los tokens solo se mantienen en memoria: no se imprimen ni se guardan. La única petición HTTP propia es a `api.anthropic.com`, sin redirecciones; las CLI gestionan sus propias conexiones de autenticación y consulta de límites. No se crean conversaciones ni se ejecutan inferencias.

Desinstalar desde el menú o con:

```sh
bash mac/uninstall.sh
```

Se eliminan únicamente la app instalada, su LaunchAgent y sus ajustes. Se conservan el repositorio, la compilación local, `~/.claude`, `~/.codex` y las CLI.

## Verificar en un Mac

- Compilar con las Command Line Tools de macOS 13 o posterior, en Apple Silicon y, si se necesita, Intel. Revisar firma local y acceso al llavero después de reinstalar.
- Instalar con ambas selecciones y con cada asistente por separado; comprobar búsqueda de CLI en Homebrew, npm y una shell de inicio. Confirmar que reinstalar conserva posición y ajustes.
- Comparar los porcentajes y reinicios con las CLI. Comprobar cuenta ausente, token caducado, llavero denegado, credencial de archivo, desconexión, HTTP 429 y `Retry-After` en segundos y fecha. Un dato ausente nunca debe aparecer como 0 %.
- Dejar vencer un reinicio: el anillo debe quedar vacío con «Falta actualizar». Revisar sondeo de 5 minutos, espera progresiva, antigüedad a los 15 minutos y actualización manual.
- Revisar anillos, logotipos, pixel art, textos, tooltips y avisos en las dos vistas, con uno y dos asistentes. Verificar nitidez en Retina y pantallas externas.
- Modificar un archivo `.jsonl` de sesiones con las CLI: la mascota debe girar, sacar la laptop y teclear. Después de 90 segundos sin actividad debe guardarla y detener el temporizador. Probar también si el directorio de sesiones se crea después de abrir el widget.
- Arrastrar desde zonas sin controles, cambiar de Space, usar pantalla completa, ocultar en ambos bordes, mover la pestaña y desconectar una pantalla. Confirmar que la ventana vuelve al área visible.
- Abrir dos veces y confirmar una sola instancia. Cerrar durante una consulta o renovación y comprobar que no quedan procesos auxiliares.
- Activar/desactivar inicio automático, cerrar sesión y comprobar la espera de 180 segundos. Desinstalar desde menú y script, verificando que no reaparece la app ni se alteran las sesiones de las CLI.
