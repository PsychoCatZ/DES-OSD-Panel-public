#requires -Version 7.0
$ErrorActionPreference = 'Stop'
# Build verification/Checks first; use its copy of the current application assembly.
Add-Type -Path (Join-Path $PSScriptRoot 'Checks\bin\Release\net8.0-windows\OsdPanel.dll')
$panelSize = [Drawing.Size]::new(345, 547)
$primary = [Drawing.Rectangle]::new(0, 0, 1920, 1040)
$left = [Drawing.Rectangle]::new(-1920, 0, 1920, 1040)
$upperRight = [Drawing.Rectangle]::new(1920, -1080, 2560, 1400)
$cases = @(
    @{ Name = 'keep left monitor position'; Method = 'Clamp'; Position = [Drawing.Point]::new(-1500, 100); Area = $left; Expected = [Drawing.Point]::new(-1500, 100) },
    @{ Name = 'keep upper right monitor position'; Method = 'Clamp'; Position = [Drawing.Point]::new(2200, -900); Area = $upperRight; Expected = [Drawing.Point]::new(2200, -900) },
    @{ Name = 'left monitor taskbar edge'; Method = 'Clamp'; Position = [Drawing.Point]::new(-100, 1000); Area = $left; Expected = [Drawing.Point]::new(-345, 493) },
    @{ Name = 'recover disconnected monitor'; Method = 'Clamp'; Position = [Drawing.Point]::new(-3000, -1000); Area = $primary; Expected = [Drawing.Point]::new(0, 0) },
    @{ Name = 'snap left monitor top left'; Method = 'NearestCorner'; Position = [Drawing.Point]::new(-1800, 50); Area = $left; Expected = [Drawing.Point]::new(-1920, 0) },
    @{ Name = 'snap upper right monitor bottom right'; Method = 'NearestCorner'; Position = [Drawing.Point]::new(4000, -300); Area = $upperRight; Expected = [Drawing.Point]::new(4135, -227) },
    @{ Name = 'oversized window remains reachable'; Method = 'Clamp'; Position = [Drawing.Point]::new(-400, -300); Area = [Drawing.Rectangle]::new(0, 0, 200, 300); Expected = [Drawing.Point]::new(0, 0) }
)
foreach ($case in $cases) {
    $actual = if ($case.Method -eq 'Clamp') { [OsdPanel.PanelPlacement]::Clamp($case.Position, $panelSize, $case.Area) } else { [OsdPanel.PanelPlacement]::NearestCorner($case.Position, $panelSize, $case.Area) }
    if ($actual -ne $case.Expected) { throw "$($case.Name): expected $($case.Expected), got $actual" }
    Write-Output "PASS: $($case.Name)"
}
