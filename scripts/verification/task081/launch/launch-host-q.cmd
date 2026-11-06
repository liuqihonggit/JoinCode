@echo off
cd /d "%~dp0..\..\..\.."
set "JCC_APP_DATA_FOLDER=%CD%\.xxx\e2e-home-q"
set "JCC_WEBVIEW_DATA_DIR=%CD%\.xxx\e2e-home-q\webview2"
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223"
set "STUB_API_KEY=stub-key"
set "JCC_DIRECTORY_PICK=%CD%\.xxx\e2e-ws-q"
"%CD%\artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe" >> "%CD%\.xxx\logs\dsh_host_q.log" 2>&1
