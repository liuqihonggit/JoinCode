@echo off
set "JCC_APP_DATA_FOLDER=D:\project\w3\.xxx\e2e-home-q"
set "JCC_WEBVIEW_DATA_DIR=D:\project\w3\.xxx\e2e-home-q\webview2"
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223"
set "STUB_API_KEY=stub-key"
set "JCC_DIRECTORY_PICK=D:\project\w3\.xxx\e2e-ws-q"
cd /d D:\project\w3
"D:\project\w3\artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe" >> "D:\project\w3\.xxx\logs\dsh_host_q.log" 2>&1
