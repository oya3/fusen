@echo off
chcp 65001 > nul
echo [fusen] 起動中...
dotnet run --project src/Fusen/Fusen.csproj
