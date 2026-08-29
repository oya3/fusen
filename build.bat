@echo off
chcp 65001 > nul
echo [fusen] Building Release package...
dotnet publish src/Fusen/Fusen.csproj -c Release -o bin/publish --nologo -v q

if %ERRORLEVEL% equ 0 (
    if exist fusen.ini (
        copy /Y fusen.ini bin\publish\fusen.ini > nul
    )
    echo [fusen] Build Succeeded!
    echo Output: bin/publish/fusen.exe
    echo Config: bin/publish/fusen.ini
) else (
    echo [fusen] Build Failed.
)
pause
