@echo off
chcp 65001 > nul
echo [fusen] ビルド中...
dotnet publish src/Fusen/Fusen.csproj -c Release -o bin/publish --nologo -v q

if %ERRORLEVEL% equ 0 (
    echo [fusen] ビルド成功！
    echo 出力先: bin/publish/fusen.exe
) else (
    echo [fusen] ビルドに失敗しました。
)
pause
