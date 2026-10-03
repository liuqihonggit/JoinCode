$procs = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*Join*' }
if ($procs) {
  $procs | Format-Table Id, ProcessName, MainWindowTitle -AutoSize
} else {
  Write-Output "No JoinCode process running"
  Write-Output "Starting GUI..."
  Start-Process "D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"
  Start-Sleep -Seconds 10
  $procs2 = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*Join*' }
  if ($procs2) {
    $procs2 | Format-Table Id, ProcessName, MainWindowTitle -AutoSize
  } else {
    Write-Output "GUI still not running after 10s"
  }
}
