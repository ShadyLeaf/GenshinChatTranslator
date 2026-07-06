param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.1",
    [string]$OutputRoot = "artifacts\package",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj"
$publishDir = Join-Path $repoRoot (Join-Path $OutputRoot "publish")
$installerOutputDir = Join-Path $repoRoot (Join-Path $OutputRoot "installer")
$installerScript = Join-Path $repoRoot "installer\GenshinChatTranslator.iss"
$setupIconFile = Join-Path $repoRoot "src\GenshinChatTranslator.App\Assets\logo.ico"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir, $installerOutputDir | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $publishDir

$translationConfigPath = Join-Path $publishDir "config\translation.yml"
if (Test-Path -LiteralPath $translationConfigPath) {
    $content = [System.IO.File]::ReadAllText($translationConfigPath, [System.Text.Encoding]::UTF8)
    $sanitized = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        "(?m)^(\s*api_key\s*:\s*).*$",
        '$1""')
    [System.IO.File]::WriteAllText($translationConfigPath, $sanitized, [System.Text.UTF8Encoding]::new($false))
}

$licenseFiles = @(
    "LICENSE",
    "THIRD_PARTY_NOTICES.md"
)
foreach ($licenseFile in $licenseFiles) {
    $source = Join-Path $repoRoot $licenseFile
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $publishDir $licenseFile) -Force
    }
}

$licensesSourceDir = Join-Path $repoRoot "licenses"
if (Test-Path -LiteralPath $licensesSourceDir) {
    Copy-Item -LiteralPath $licensesSourceDir -Destination (Join-Path $publishDir "licenses") -Recurse -Force
}

if ($SkipInstaller) {
    Write-Host "Published sanitized app to: $publishDir"
    return
}

$isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
$isccPath = if ($null -eq $isccCommand) { $null } else { $isccCommand.Source }
if ($null -eq $isccPath) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (Test-Path -LiteralPath $candidate) {
        $isccPath = $candidate
    }
}

if ($null -eq $isccPath) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup or rerun with -SkipInstaller to produce only the sanitized publish directory."
}

& $isccPath `
    "/DAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$installerOutputDir" `
    "/DSetupIconFile=$setupIconFile" `
    $installerScript

Write-Host "Published sanitized app to: $publishDir"
Write-Host "Created installer in: $installerOutputDir"
