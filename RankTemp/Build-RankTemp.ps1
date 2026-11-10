[CmdletBinding()]
param([switch]$IncludeSensorDriver)

$ErrorActionPreference = 'Stop'
$version = '0.9.6'
$archiveHash = '086d9f1b5a99e643edc2cfaaac16051685b551e4c5ac0b32a57c58c0e529c001'
$driverVersion = '2.2.0'
$driverHash = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'Se necesita el compilador de .NET Framework de 64 bits.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = (New-Item -ItemType Directory -Path (Join-Path $tempRoot ('ranktemp-build-' + [guid]::NewGuid()))).FullName
try {
    $archive = Join-Path $stage 'LibreHardwareMonitor.zip'
    $libraryFolder = Join-Path $stage 'library'
    $appFolder = (New-Item -ItemType Directory -Path (Join-Path $stage 'RankTemp')).FullName
    $notices = (New-Item -ItemType Directory -Path (Join-Path $appFolder 'licencias')).FullName
    $dist = Join-Path $PSScriptRoot 'dist'

    Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v$version/LibreHardwareMonitor.zip" -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) {
        throw 'El SHA-256 de LibreHardwareMonitor no coincide con el publicado.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $libraryFolder

    $libraries = @(
        'LibreHardwareMonitorLib', 'BlackSharp.Core', 'DiskInfoToolkit', 'HidSharp', 'RAMSPDToolkit-NDD',
        'System.Buffers', 'System.Memory', 'System.Numerics.Vectors', 'System.Runtime.CompilerServices.Unsafe'
    )
    foreach ($name in $libraries) {
        Copy-Item -LiteralPath (Join-Path $libraryFolder "$name.dll") -Destination $appFolder
    }
    foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
        Invoke-WebRequest -UseBasicParsing -Uri "https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v$version/$name" -OutFile (Join-Path $notices $name)
    }

    if ($IncludeSensorDriver) {
        $driverFolder = (New-Item -ItemType Directory -Path (Join-Path $appFolder 'controlador')).FullName
        $driver = Join-Path $driverFolder 'PawnIO_setup.exe'
        Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/namazso/PawnIO.Setup/releases/download/$driverVersion/PawnIO_setup.exe" -OutFile $driver
        if ((Get-FileHash -LiteralPath $driver -Algorithm SHA256).Hash -ne $driverHash) {
            throw 'El SHA-256 de PawnIO no coincide con el publicado.'
        }
    }

    $source = Join-Path $PSScriptRoot 'src'
    $sources = @(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName)
    $exe = Join-Path $appFolder 'RankTemp.exe'
    $reference = Join-Path $appFolder 'LibreHardwareMonitorLib.dll'
    $manifest = Join-Path $source 'RankTemp.manifest'
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /codepage:65001 "/out:$exe" "/reference:$reference" "/win32manifest:$manifest" /reference:Microsoft.CSharp.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll @sources
    if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar RankTemp.' }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LEEME.txt') -Destination $appFolder
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $package = Join-Path $dist 'RankTemp-Windows-x64.zip'
    Compress-Archive -LiteralPath $appFolder -DestinationPath $package -Force
    Write-Host "ZIP generado en $package"
} finally {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    if ([IO.Path]::GetDirectoryName($resolvedStage) -eq $tempRoot.TrimEnd('\') -and
        [IO.Path]::GetFileName($resolvedStage) -like 'ranktemp-build-*') {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
