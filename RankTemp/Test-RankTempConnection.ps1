[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^COM[1-9][0-9]*$')]
    [string]$Port,

    [switch]$TestDisplay
)

$reportPath = Join-Path $PSScriptRoot 'RankTemp-conexion.txt'
$report = [System.Collections.Generic.List[string]]::new()
$report.Add("RankTemp 1019 | $Port | 115200 8N1")
$serial = [System.IO.Ports.SerialPort]::new(
    $Port, 115200, [System.IO.Ports.Parity]::None, 8, [System.IO.Ports.StopBits]::One
)
try {
    $serial.ReadTimeout = 2000
    $serial.WriteTimeout = 2000
    $serial.Handshake = [System.IO.Ports.Handshake]::None
    $serial.Open()

    $probe = [byte[]]@(0x02, 0x01, 0x00)
    $report.Add('TX 02 01 00')
    $serial.Write($probe, 0, $probe.Length)
    $response = $serial.ReadByte()
    $report.Add(('RX {0:X2}' -f $response))
    if ($response -ne 0x31) {
        throw ('Respuesta inesperada. Se esperaba 31 y se recibio {0:X2}.' -f $response)
    }
    $report.Add('OK: respuesta de arranque confirmada.')

    if ($TestDisplay) {
        # Recorded telemetry, with only the CPU temperature word varied.
        $frame = [byte[]]@(0x01, 0x0A, 0x02, 0x58, 0x0A, 0xE5, 0x0A, 0xE5, 0x01, 0xA4, 0x05, 0x32)
        $report.Add('Prueba de pantalla con datos grabados.')
        foreach ($temperature in @(600, 65535, 600)) {
            $frame[2] = [byte]($temperature -shr 8)
            $frame[3] = [byte]($temperature -band 0xff)
            for ($second = 0; $second -lt 3; $second++) {
                $report.Add(('TX {0}' -f ([BitConverter]::ToString($frame).Replace('-', ' '))))
                $serial.Write($frame, 0, $frame.Length)
                $response = $serial.ReadByte()
                $report.Add(('RX {0:X2}' -f $response))
                if ($response -ne 0x21) {
                    throw ('Respuesta de telemetria inesperada. Se esperaba 21 y se recibio {0:X2}.' -f $response)
                }
                Start-Sleep -Seconds 1
            }
        }
        $report.Add('OK: el dispositivo respondio 21 a las nueve tramas.')
    }
} catch {
    $report.Add("ERROR: $($_.Exception.Message)")
    throw
} finally {
    $serial.Dispose()
    $report | Set-Content -LiteralPath $reportPath -Encoding UTF8 -ErrorAction Stop
    $report | ForEach-Object { Write-Host $_ }
    Write-Host "Informe guardado en $reportPath"
}
