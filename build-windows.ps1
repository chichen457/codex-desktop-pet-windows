$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$args = @(
  '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
  "/out:$root\Codex桌宠-v4.8.exe",
  '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
  '/reference:C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF\System.Speech.dll',
  "/resource:$root\assets\codex-spritesheet-windows.png,CodexPet.atlas.png",
  "/resource:$root\wordbooks.tsv,CodexPet.words.tsv",
  "$root\CodexPetWindows.cs"
)
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "C# 编译失败：$LASTEXITCODE" }
Write-Host "已生成：$root\Codex桌宠-v4.8.exe"
