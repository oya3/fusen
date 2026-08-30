<#
.SYNOPSIS
    fusen を Windows ログオン時に自動起動するよう設定します。

.DESCRIPTION
    実行したユーザー自身のスタートアップフォルダに fusen.exe へのショートカットを作成します。
    配置先は環境変数を直書きせず [Environment]::GetFolderPath('Startup') で解決するため、
    ユーザー名が何であっても、またフォルダリダイレクトが設定された環境でも正しい場所になります。

    このスクリプトは fusen.exe と同じフォルダに置いて実行してください。
    ショートカットの参照先は「このスクリプトが置かれているフォルダの fusen.exe」になります。

    なお fusen 本体はレジストリや %APPDATA% を一切使いません。
    自動起動の設定はこのスクリプトを実行したときにのみ行われ、-Remove でいつでも元に戻せます。

.PARAMETER Remove
    自動起動の設定を解除します（作成したショートカットを削除します）。

.EXAMPLE
    .\setup-autostart.ps1
    自動起動を設定します。

.EXAMPLE
    .\setup-autostart.ps1 -Remove
    自動起動を解除します。
#>
[CmdletBinding()]
param(
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$ExeName      = 'fusen.exe'
$ShortcutName = 'fusen.lnk'

# このスクリプトが置かれているフォルダ = fusen.exe があるフォルダ
$AppDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExePath  = Join-Path $AppDir $ExeName

# 実行したユーザー自身のスタートアップフォルダ
# ( 通常は C:\Users\<ユーザー名>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup )
$StartupDir  = [Environment]::GetFolderPath('Startup')
$ShortcutPath = Join-Path $StartupDir $ShortcutName

Write-Host ''
Write-Host '  fusen 自動起動の設定' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------'
Write-Host ("  スタートアップ : {0}" -f $StartupDir)
Write-Host ''

# ---- 解除 ----
if ($Remove) {
    if (Test-Path -LiteralPath $ShortcutPath) {
        Remove-Item -LiteralPath $ShortcutPath -Force
        Write-Host '  [OK] 自動起動を解除しました。' -ForegroundColor Green
        Write-Host ("       削除したショートカット: {0}" -f $ShortcutPath)
    }
    else {
        Write-Host '  [--] 自動起動は設定されていません。何もしませんでした。' -ForegroundColor Yellow
    }
    Write-Host ''
    exit 0
}

# ---- 設定 ----
if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Host ("  [NG] {0} が見つかりません。" -f $ExeName) -ForegroundColor Red
    Write-Host ("       探した場所: {0}" -f $ExePath)
    Write-Host '       このスクリプトは fusen.exe と同じフォルダに置いて実行してください。'
    Write-Host ''
    exit 1
}

if (-not (Test-Path -LiteralPath $StartupDir)) {
    New-Item -ItemType Directory -Path $StartupDir -Force | Out-Null
}

$alreadyExists = Test-Path -LiteralPath $ShortcutPath

$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath       = $ExePath
    # 作業フォルダをアプリ直下にしておく。fusen はポータブル動作のため
    # data/ と fusen.ini を実行ファイルと同じ場所から読む。
    $shortcut.WorkingDirectory = $AppDir
    $shortcut.IconLocation     = $ExePath
    $shortcut.Description      = 'fusen - デスクトップ付箋'
    $shortcut.Save()
}
finally {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

if ($alreadyExists) {
    Write-Host '  [OK] 自動起動の設定を更新しました。' -ForegroundColor Green
}
else {
    Write-Host '  [OK] 自動起動を設定しました。' -ForegroundColor Green
}

Write-Host ("       ショートカット : {0}" -f $ShortcutPath)
Write-Host ("       起動する実行体 : {0}" -f $ExePath)
Write-Host ''
Write-Host '  次回の Windows ログオン時から fusen が自動で起動します。'
Write-Host '  解除するには -Remove を付けて実行してください。'
Write-Host ''
exit 0
