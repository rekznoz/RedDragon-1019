[CmdletBinding()]
param()

$reportPath = Join-Path $PSScriptRoot 'RankTemp-diagnostico.txt'
$driverUsbIds = @('1A86:7523', '1A86:5523', '1A86:7522', '1A86:E523', '4348:5523')
try {
    $devices = Get-PnpDevice -PresentOnly -ErrorAction Stop
} catch {
    "Error al consultar los dispositivos USB. $($_.Exception.Message)" |
        Set-Content -LiteralPath $reportPath -Encoding UTF8 -ErrorAction Stop
    Write-Host "Diagnostico guardado en $reportPath"
    throw
}
$candidates = foreach ($device in $devices) {
    if ($device.InstanceId -notmatch '^USB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?:&|\\|$)') {
        continue
    }

    $usbId = ('{0}:{1}' -f $Matches[1], $Matches[2]).ToUpperInvariant()
    if ($usbId -notin $driverUsbIds) {
        continue
    }

    $port = $null
    if ($device.FriendlyName -match '\((COM[0-9]+)\)\s*$') {
        $port = $Matches[1]
    }

    [pscustomobject]@{
        UsbId = $usbId
        Port = $port
        Name = $device.FriendlyName
        Status = $device.Status
    }
}

if ($candidates) {
    $sortedCandidates = $candidates | Sort-Object UsbId, Port
    $report = $sortedCandidates | Format-List UsbId, Port, Name, Status | Out-String -Width 4096
    $report = $report.Trim()
    $sortedCandidates
} else {
    $report = 'No se encontraron dispositivos USB compatibles con el controlador incluido en CpuTemp.'
    Write-Host $report
}

$report | Set-Content -LiteralPath $reportPath -Encoding UTF8 -ErrorAction Stop
Write-Host "Diagnostico guardado en $reportPath"
