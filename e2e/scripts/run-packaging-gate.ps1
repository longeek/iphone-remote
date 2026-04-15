# Packaging gate: run automated checks before release / AAB.
# Requires: Node 18+, .NET 8 SDK (for host), Java 17 + Android SDK (for Gradle).
# Maestro is optional (install from https://maestro.mobile.dev/).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

# ── Step 0: Verify project file integrity ──
Write-Host "`n== Verify project files (search 'iphone-remote' structure) ==" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "verify-project-files.ps1") -Root $root
if ($LASTEXITCODE -ne 0) { exit 1 }

# ── Step 1: Signaling unit tests ──
Write-Host "`n== Signaling unit tests ==" -ForegroundColor Cyan
Push-Location server/signaling
if (-not (Test-Path node_modules)) { npm install }
npm test
Pop-Location

# ── Step 1b: E2E signaling integration test ──
Write-Host "`n== E2E signaling integration test (Android client ↔ Windows host) ==" -ForegroundColor Cyan
Push-Location e2e/scripts
if (-not (Test-Path node_modules)) { npm install }
node --test e2e-signaling-test.mjs
Pop-Location

# ── Step 2: Windows host build + unit tests ──
Write-Host "`n== Windows host build ==" -ForegroundColor Cyan
Push-Location host/windows/src/RemoteHost
dotnet restore
dotnet build -c Release
Pop-Location

Write-Host "`n== Windows host unit tests ==" -ForegroundColor Cyan
Push-Location host/windows/src/RemoteHost.Tests
dotnet restore
dotnet test -c Release --no-build
Pop-Location

# ── Step 3: Android JVM unit tests ──
Write-Host "`n== Android JVM unit tests ==" -ForegroundColor Cyan
Push-Location apps/android
if (-not (Test-Path gradlew.bat)) {
    Write-Host "Generating Gradle wrapper (one-time)..." -ForegroundColor Yellow
    if (Get-Command gradle -ErrorAction SilentlyContinue) {
        gradle wrapper --gradle-version 8.2
    } else {
        $zip = Join-Path $PWD "gradle-8.2-bin.zip"
        if (Test-Path $zip) {
            Expand-Archive -Path $zip -DestinationPath $PWD -Force
            $gradleBat = Join-Path $PWD "gradle-8.2\bin\gradle.bat"
            & $gradleBat wrapper --gradle-version 8.2
        } else {
            Write-Warning "No Gradle wrapper and no local zip. Install Gradle or download gradle-8.2-bin.zip into apps/android."
            exit 2
        }
    }
}
.\gradlew.bat :app:testDebugUnitTest --no-daemon
Pop-Location

# ── Step 4: Android assemble (incl. androidTest) ──
Write-Host "`n== Android assemble (incl. androidTest) ==" -ForegroundColor Cyan
Push-Location apps/android
.\gradlew.bat :app:assembleDebug :app:assembleDebugAndroidTest --no-daemon
Pop-Location

# ── Step 5: Optional Maestro ──
Write-Host "`n== Optional: Maestro (if installed, device/emulator connected) ==" -ForegroundColor Cyan
if (Get-Command maestro -ErrorAction SilentlyContinue) {
    maestro test e2e/maestro/android-login-and-session.yaml
    maestro test e2e/maestro/android-session-disconnect.yaml
} else {
    Write-Host "Skip Maestro (not installed)." -ForegroundColor DarkGray
}

Write-Host "`nPackaging gate finished OK." -ForegroundColor Green