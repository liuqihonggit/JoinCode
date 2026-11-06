@echo off
cd /d "%~dp0..\..\..\.."
set "JCC_APP_DATA_FOLDER=%CD%\.xxx\c8-home"
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9224"
set "STUB_API_KEY=stub-key"
set "DEEPSEEK_API_KEY="
"%CD%\artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe" >> "%CD%\.xxx\logs\dsh_host_c8.log" 2>&1
