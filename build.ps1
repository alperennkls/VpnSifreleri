# Builds VpnSifreleri.exe with the C# compiler that ships with Windows (.NET Framework 4.x),
# so nothing has to be installed. The tests run first; a failure stops the build.
# Keep this file ASCII: Windows PowerShell 5.1 reads scripts without a BOM as ANSI.
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$obj = Join-Path $root 'obj'
$exe = Join-Path $root 'VpnSifreleri.exe'
$ico = Join-Path $obj 'VpnSifreleri.ico'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$common = '/nologo', '/codepage:65001', '/optimize+', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Security.dll'
# Every .cs file goes into both builds, except the other build's entry points
$sources = @(Get-ChildItem $root -Filter '*.cs' | Where-Object { $_.Extension -eq '.cs' } | ForEach-Object { $_.FullName })
$testSources = @($sources | Where-Object { (Split-Path $_ -Leaf) -notin 'TrayApp.cs', 'AssemblyInfo.cs' })
$appSources = @($sources | Where-Object { (Split-Path $_ -Leaf) -ne 'Tests.cs' })

function Invoke-Csc([string[]] $arguments) {
    & $csc @common @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Derleme hatasi' }
}

New-Item -ItemType Directory -Force $obj | Out-Null

# 1. Tests
Invoke-Csc (@('/target:exe', "/out:$obj\Tests.exe") + $testSources)
& "$obj\Tests.exe" $root
if ($LASTEXITCODE -ne 0) { throw 'Testler basarisiz, exe uretilmedi' }

# 2. The app draws its own icon: build once without it and export the .ico
Invoke-Csc (@('/target:winexe', "/out:$obj\IconMaker.exe") + $appSources)
$maker = Start-Process -FilePath "$obj\IconMaker.exe" -ArgumentList '--make-icon', "`"$ico`"" -Wait -PassThru
if ($maker.ExitCode -ne 0) { throw 'Ikon uretilemedi' }

# 3. Final exe; a running copy would keep the file locked
# Only a copy started from this exe: a build in another checkout must leave the user's running app alone
$running = @(Get-Process -Name 'VpnSifreleri' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($running.Count -gt 0) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 5
}
Invoke-Csc (@('/target:winexe', "/win32icon:$ico", "/out:$exe") + $appSources)
Write-Host "Hazir: $exe"
