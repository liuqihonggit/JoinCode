@echo off
cd /d "%~dp0..\..\..\.."
set "JCC_APP_DATA_FOLDER=%CD%\.xxx\c7-home"
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223"
set "STUB_API_KEY=stub-key"
"%CD%\artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe" >> "%CD%\.xxx\logs\dsh_host_c7g.log" 2>&1
