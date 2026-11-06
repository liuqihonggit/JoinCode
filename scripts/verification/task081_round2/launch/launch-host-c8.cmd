@echo off
set "JCC_APP_DATA_FOLDER=D:\project\w3\.xxx\c8-home"
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9224"
set "STUB_API_KEY=stub-key"
set "DEEPSEEK_API_KEY="
cd /d D:\project\w3
"D:\project\w3\artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe" >> "D:\project\w3\.xxx\logs\dsh_host_c8.log" 2>&1
