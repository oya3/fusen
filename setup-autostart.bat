@echo off
chcp 65001 > nul
rem ============================================================================
rem  fusen 自動起動の設定 / 解除
rem
rem  そのまま実行すると、Windows ログオン時に fusen が自動起動するようになります。
rem  解除する場合は次のように引数を付けて実行してください。
rem
rem      setup-autostart.bat -Remove
rem
rem  .ps1 を直接ダブルクリックしても既定の実行ポリシーで弾かれるため、
rem  この .bat から -ExecutionPolicy Bypass を指定して呼び出しています。
rem ============================================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-autostart.ps1" %*

pause
