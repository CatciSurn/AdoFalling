# Builds the AdoFalling BepInEx plugin and stages it into the game's BepInEx\plugins folder.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1            # build only
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Install   # build + copy to the game
#
param(
    [switch]$Install,
    [switch]$InstallLoader,
    [string]$GameDir = "D:\steam\steamapps\common\A Dance of Fire and Ice"
)

$ErrorActionPreference = "Stop"

# Absolute paths are resolved from this script's own location so that invoking the
# script never has to pass a non-ASCII path through the command line.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $here "src\AdoFalling"
$OutDir = Join-Path $here "build"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$managed = Join-Path $GameDir "A Dance of Fire and Ice_Data\Managed"

# BepInEx's runtime assemblies: the ones installed in the game win, so a build always uses
# exactly what the game will load.
$bepCore = Join-Path $GameDir "BepInEx\core"
if (-not (Test-Path $bepCore))
{
    $bepCore = Join-Path $here "bepinex\BepInEx\core"
}
$ToolsDir = Join-Path $here "bepinex"

foreach ($p in @($csc, $managed, $bepCore)) {
    if (-not (Test-Path $p)) { throw "missing required path: $p" }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$dll = Join-Path $OutDir "AdoFalling.dll"

# Reference list: the netstandard facade plus Unity/game/BepInEx assemblies. The game's
# mscorlib/System are deliberately not referenced (the compiler already supplies the
# framework ones; netstandard.dll forwards the rest), which avoids CS1703 duplicates.
$refs = New-Object System.Collections.Generic.List[string]
$refs.Add((Join-Path $managed "netstandard.dll"))
$refs.Add((Join-Path $managed "System.Runtime.dll"))
Get-ChildItem $managed -Filter "UnityEngine*.dll" | ForEach-Object { $refs.Add($_.FullName) }
foreach ($n in @("Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll", "Newtonsoft.Json.dll")) {
    $refs.Add((Join-Path $managed $n))
}
foreach ($n in @("BepInEx.dll", "0Harmony.dll", "BepInEx.Harmony.dll")) {
    $refs.Add((Join-Path $bepCore $n))
}
$refs = $refs | Where-Object { Test-Path $_ } | Select-Object -Unique

$sources = Get-ChildItem $SrcDir -Filter *.cs | Select-Object -ExpandProperty FullName
if (-not $sources) { throw "no sources found in $SrcDir" }

$refArg = ($refs | ForEach-Object { '"' + $_ + '"' }) -join ","
Write-Host "compiling $($sources.Count) source file(s) -> $dll"
$cscLog = Join-Path $OutDir "csc.log"
& $csc /nologo /target:library /langversion:5 /optimize+ `
    /out:"$dll" `
    /reference:$refArg `
    $sources 2>&1 | Tee-Object -FilePath $cscLog
if ($LASTEXITCODE -ne 0) { throw "compilation failed ($LASTEXITCODE), see $cscLog" }
if (-not (Test-Path $dll)) { throw "compiler produced no output" }
Write-Host ("built {0} ({1:N0} bytes)" -f $dll, (Get-Item $dll).Length)

if ($InstallLoader) {
    Write-Host "installing BepInEx loader into $GameDir"
    Copy-Item (Join-Path $ToolsDir "*") $GameDir -Recurse -Force
}
if ($Install) {
    $plugins = Join-Path $GameDir "BepInEx\plugins"
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    Copy-Item $dll $plugins -Force
    Write-Host "installed -> $plugins"
}
