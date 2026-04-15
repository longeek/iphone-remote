# Run E2E on a single connected Android device (USB or wireless adb).
# Usage: .\e2e\scripts\run-android-e2e-on-device.ps1
# Prereq: adb devices shows one "device"; optional: Maestro in PATH for Maestro flows.

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$android = Join-Path $root "apps\android"

$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

function Ensure-GradleWrapper {
    param([string]$ProjectDir)
    if (Test-Path (Join-Path $ProjectDir "gradlew.bat")) { return }
    Write-Host "[gradle] Creating wrapper (one-time download)..." -ForegroundColor Cyan
    $zip = Join-Path $ProjectDir "gradle-8.2-bin.zip"
    $uri = "https://services.gradle.org/distributions/gradle-8.2-bin.zip"
    if (-not (Test-Path $zip)) {
        Invoke-WebRequest -Uri $uri -OutFile $zip -UseBasicParsing
    }
    Expand-Archive -Path $zip -DestinationPath $ProjectDir -Force
    $gradleBat = Join-Path $ProjectDir "gradle-8.2\bin\gradle.bat"
    Push-Location $ProjectDir
    try {
        & $gradleBat wrapper --gradle-version 8.2
        if (-not (Test-Path ".\gradlew.bat")) { throw "gradlew.bat not created" }
    } finally {
        Pop-Location
    }
}

# ── Step 0: Signaling unit tests + E2E ──
Write-Host "`n== Signaling unit tests ==" -ForegroundColor Cyan
Push-Location (Join-Path $root "server\signaling")
if (-not (Test-Path node_modules)) { npm install }
npm test
Pop-Location

Write-Host "`n== E2E signaling test (Android client <-> Windows host) ==" -ForegroundColor Cyan
Push-Location (Join-Path $root "e2e\scripts")
if (-not (Test-Path node_modules)) { npm install }
node --test e2e-signaling-test.mjs
Pop-Location

# ── Step 1: Android JVM unit tests ──
Write-Host "`n== Android JVM unit tests ==" -ForegroundColor Cyan
Ensure-GradleWrapper $android
Push-Location $android
try {
    .\gradlew.bat :app:testDebugUnitTest --no-daemon
} finally {
    Pop-Location
}

# ── Step 2: Build & install debug APK ──
Write-Host "`n== adb devices ==" -ForegroundColor Cyan
adb devices -l
$lines = adb devices | Where-Object { $_ -match "`tdevice$" }
if (-not $lines) {
    Write-Host @"

No device yet. If you only ran 'adb pair IP:PAIR_PORT' successfully, you still need the
**connection** address from: Settings -> Developer options -> Wireless debugging
(look for 'IP address & port' — NOT the pairing port).

Example:
  adb connect 192.168.0.2:35757

Then re-run this script.
"@ -ForegroundColor Yellow
    exit 1
}

Write-Host "`n== Gradle: installDebug ==" -ForegroundColor Cyan
Push-Location $android
try {
    .\gradlew.bat :app:installDebug --no-daemon
    Write-Host "`n== Android Instrumentation (connectedDebugAndroidTest) ==" -ForegroundColor Cyan
    .\gradlew.bat :app:connectedDebugAndroidTest --no-daemon
} finally {
    Pop-Location
}

# ── Step 3: Maestro flows ──
if (Get-Command maestro -ErrorAction SilentlyContinue) {
    Write-Host "`n== Maestro flows ==" -ForegroundColor Cyan
    $maestroRoot = Join-Path $root "e2e\maestro"
    maestro (Join-Path $maestroRoot "android-login-and-session.yaml")
    maestro (Join-Path $maestroRoot "android-session-disconnect.yaml")
    maestro (Join-Path $maestroRoot "android-remote-touch-sim.yaml")
} else {
    Write-Host "`n[skip] Maestro not in PATH. Install: https://maestro.mobile.dev/" -ForegroundColor DarkGray
}

Write-Host "`nDone." -ForegroundColor Green