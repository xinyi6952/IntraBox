$exe = Join-Path $PSScriptRoot "..\src\IntraBox\bin\Release\IntraBox.exe"
[System.Reflection.Assembly]::LoadFrom($exe) | Out-Null

$f = New-Object IntraBox.Modules.Formatter.SqlFormatter
$sql = "SELECT u.id, u.username, u.email, r.role_name FROM users u LEFT JOIN user_roles ur ON u.id = ur.user_id LEFT JOIN roles r ON ur.role_id = r.id WHERE u.status = 1 AND u.create_time >= '2025-01-01' ORDER BY u.id DESC LIMIT 20;"
$err = $null
$result = $f.Beautify($sql, "    ", [ref]$err)
if ($err) {
    Write-Host "ERROR: $err"
} else {
    Write-Host "=== 输出 ==="
    Write-Host $result
}
