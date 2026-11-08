# Instala Usage Orbit para el usuario actual: compila el widget, lo copia a %LOCALAPPDATA%\UsageOrbit,
# crea los accesos directos y el inicio automatico, y abre el inicio de sesion de las CLI elegidas.
# No necesita permisos de administrador. Se puede repetir para actualizar o cambiar las opciones.
param(
    [ValidateSet('Logotipos', 'Mascotas', 'Ambas')][string]$Variante,
    [ValidateSet('Ambos', 'Claude', 'Codex')][string]$Asistentes,
    [switch]$SinSesion,
    [switch]$NoAbrir
)
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDir = Join-Path $env:LOCALAPPDATA 'UsageOrbit'

function Select-Option([string]$title, [string[]]$options) {
    Write-Host ''
    Write-Host $title
    for ($i = 0; $i -lt $options.Length; $i++) { Write-Host ("  {0}) {1}" -f ($i + 1), $options[$i]) }
    while ($true) {
        $answer = Read-Host 'Elige un numero'
        $number = 0
        if ([int]::TryParse($answer, [ref]$number) -and $number -ge 1 -and $number -le $options.Length) { return $options[$number - 1] }
    }
}

# Busca la CLI en el PATH, en la carpeta global de npm y en la del instalador nativo de Claude Code.
function Find-Cli([string]$name) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    foreach ($path in @((Join-Path $env:APPDATA "npm\$name.cmd"), (Join-Path $env:USERPROFILE ".local\bin\$name.exe"))) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

# Devuelve la ruta de la CLI; si falta, ofrece instalarla con npm.
function Get-Cli([string]$name, [string]$package) {
    $cli = Find-Cli $name
    if ($cli) { return $cli }
    Write-Host ''
    Write-Host "No se encontro la CLI '$name'."
    if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
        Write-Host 'Instala Node.js desde https://nodejs.org y vuelve a ejecutar el instalador para iniciar sesion.'
        return $null
    }
    $answer = Read-Host "Instalarla ahora con 'npm install -g $package'? (s/n)"
    if ($answer -notmatch '^[sS]') { return $null }
    & npm.cmd install -g $package
    return Find-Cli $name
}

function Start-ClaudeLogin {
    $cli = Get-Cli 'claude' '@anthropic-ai/claude-code'
    if (-not $cli) { Write-Host 'Claude quedara sin datos hasta que instales su CLI e inicies sesion.'; return }
    # Se consulta a traves de cmd: PowerShell 5.1 convierte en error cualquier texto de stderr.
    $status = (& cmd.exe /c "`"$cli`" auth status 2>nul") -join "`n"
    if ($status -match '"loggedIn":\s*true') { Write-Host 'Claude Code ya tiene una sesion iniciada.'; return }
    Write-Host ''
    Write-Host 'Inicia sesion en Claude Code (se abrira el navegador)...'
    & $cli auth login
}

function Start-CodexLogin {
    $cli = Get-Cli 'codex' '@openai/codex'
    if (-not $cli) { Write-Host 'Codex quedara sin datos hasta que instales su CLI e inicies sesion.'; return }
    & cmd.exe /c "`"$cli`" login status >nul 2>&1"
    if ($LASTEXITCODE -eq 0) { Write-Host 'Codex ya tiene una sesion iniciada.'; return }
    Write-Host ''
    Write-Host 'Inicia sesion en Codex (se abrira el navegador)...'
    & $cli login
}

Write-Host 'Instalador de Usage Orbit'
if (-not $Variante) { $Variante = Select-Option 'Apariencia (con Ambas puedes cambiarla desde el widget):' @('Logotipos', 'Mascotas', 'Ambas') }
if (-not $Asistentes) { $Asistentes = Select-Option 'Asistentes que quieres ver:' @('Ambos', 'Claude', 'Codex') }

Write-Host ''
Write-Host 'Compilando...'
& cmd.exe /c "`"$source\build.cmd`""
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el widget.' }

# Si el widget ya estaba abierto o esperando su inicio diferido, se cierra para poder reemplazarlo.
foreach ($signalName in @('Local\UsageOrbit.Exit.v1', 'Local\UsageOrbit.CancelDelay.v1')) {
    try {
        $signal = [System.Threading.EventWaitHandle]::OpenExisting($signalName)
        $signal.Set() | Out-Null
        $signal.Dispose()
    } catch [System.Threading.WaitHandleCannotBeOpenedException] { }
}
Get-Process UsageOrbit -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir\*" } |
    Wait-Process -Timeout 15 -ErrorAction SilentlyContinue

$installer = Start-Process -FilePath (Join-Path $source 'bin\UsageOrbit.exe') -ArgumentList '--instalar' -Wait -PassThru
if ($installer.ExitCode -ne 0) { throw "No se pudo instalar. Revisa $installDir\install.log" }

# Guarda las opciones elegidas conservando la posicion de una instalacion anterior.
$settingsPath = Join-Path $installDir 'settings.json'
$settings = [ordered]@{}
if (Test-Path $settingsPath) {
    try { (Get-Content $settingsPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $settings[$_.Name] = $_.Value } } catch { }
}
if ($Variante -eq 'Logotipos') { $settings['Variant'] = 0 }
elseif ($Variante -eq 'Mascotas' -or $null -eq $settings['Variant']) { $settings['Variant'] = 1 }
$settings['FixedVariant'] = $Variante -ne 'Ambas'
$settings['Show'] = @{ Ambos = 0; Claude = 1; Codex = 2 }[$Asistentes]
$settings | ConvertTo-Json | Set-Content -Path $settingsPath -Encoding UTF8

if (-not $SinSesion) {
    if ($Asistentes -ne 'Codex') { Start-ClaudeLogin }
    if ($Asistentes -ne 'Claude') { Start-CodexLogin }
}

Write-Host ''
Write-Host "Listo. Usage Orbit quedo instalado en $installDir"
Write-Host 'Se abre ahora y, desde el proximo inicio de sesion de Windows, tres minutos despues de entrar.'
Write-Host 'Para desinstalarlo usa desinstalar.cmd o "Desinstalar Usage Orbit" en el menu Inicio.'
if (-not $NoAbrir) { Start-Process (Join-Path $installDir 'UsageOrbit.exe') }
