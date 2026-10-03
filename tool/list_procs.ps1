$procs = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*Join*' -or $_.ProcessName -like '*jcc*' }
if ($procs) {
  $procs | Format-Table Id, ProcessName, MainWindowTitle -AutoSize
} else {
  Write-Output "No process running"
}
