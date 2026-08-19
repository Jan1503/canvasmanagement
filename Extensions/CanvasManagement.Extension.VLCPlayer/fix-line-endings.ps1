# Fix Line Endings for test-hardware-decode.sh
# Run this on Raspberry Pi

Write-Host "Converting line endings from Windows (CRLF) to Unix (LF)..."

$scriptPath = "./test-hardware-decode.sh"

if (Test-Path $scriptPath) {
    # Read the file
    $content = Get-Content $scriptPath -Raw
    
    # Replace CRLF with LF
    $content = $content -replace "`r`n", "`n"
    
    # Write back without BOM
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($scriptPath, $content, $utf8NoBom)
    
    Write-Host "✓ Line endings converted successfully"
    Write-Host ""
    Write-Host "On Linux/Pi, now run:"
    Write-Host "  chmod +x test-hardware-decode.sh"
    Write-Host "  ./test-hardware-decode.sh"
} else {
    Write-Host "✗ File not found: $scriptPath"
}
