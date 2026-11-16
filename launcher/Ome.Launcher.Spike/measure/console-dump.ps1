# Writes the text of the console this script runs in to a file, so a measurement can see what a windowed program
# printed there. Waits first, for a program the shell did not wait for. Usage: console-dump.ps1 OUT [WAIT_MS]
param([string]$Out, [int]$WaitMs = 2500)
Start-Sleep -Milliseconds $WaitMs
$raw = $Host.UI.RawUI
$width = $raw.BufferSize.Width
$bottom = $raw.CursorPosition.Y
$rect = New-Object System.Management.Automation.Host.Rectangle 0, 0, ($width - 1), $bottom
$cells = $raw.GetBufferContents($rect)
$lines = for ($y = 0; $y -le $bottom; $y++) {
    $text = New-Object System.Text.StringBuilder
    for ($x = 0; $x -lt $width; $x++) {
        $cell = $cells[$y, $x]
        if ($cell.BufferCellType -ne 'Trailing') { [void]$text.Append($cell.Character) }
    }
    $text.ToString().TrimEnd()
}
$lines | Where-Object { $_ } | Set-Content -Encoding utf8 $Out
