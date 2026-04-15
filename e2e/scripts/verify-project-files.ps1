# Verify project file integrity — simulates "search project name in D: drive".
# Scans D:\codes\iphone-remote for all expected source, test, config, and infra files.
# Exit code 1 if any file is missing; 0 if all present.
# Usage: .\e2e\scripts\verify-project-files.ps1 [-Root D:\codes\iphone-remote]

param(
    [string]$Root = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = "Stop"

$expected = @(
    # ── Signaling server ──
    "server/signaling/src/index.js",
    "server/signaling/src/start.js",
    "server/signaling/test/protocol.test.js",
    "server/signaling/package.json",
    "server/signaling/package-lock.json",

    # ── Signaling docs ──
    "docs/SIGNALING_PROTOCOL.md",

    # ── Windows host ──
    "host/windows/src/RemoteHost/Program.cs",
    "host/windows/src/RemoteHost/ArgParser.cs",
    "host/windows/src/RemoteHost/ControlMessages.cs",
    "host/windows/src/RemoteHost/InputInjector.cs",
    "host/windows/src/RemoteHost/RemoteHostRunner.cs",
    "host/windows/src/RemoteHost/HostOptions.cs",
    "host/windows/src/RemoteHost/ProbeServer.cs",
    "host/windows/src/RemoteHost/DesktopCapture.cs",
    "host/windows/src/RemoteHost/RemoteHost.csproj",
    "host/windows/iphone-remote-host.sln",

    # ── Windows host tests ──
    "host/windows/src/RemoteHost.Tests/ControlMessagesTest.cs",
    "host/windows/src/RemoteHost.Tests/ProbeServerTest.cs",
    "host/windows/src/RemoteHost.Tests/ArgParserTest.cs",
    "host/windows/src/RemoteHost.Tests/RemoteHost.Tests.csproj",

    # ── Android app ──
    "apps/android/app/src/main/java/com/iphoneremote/remote/MainActivity.kt",
    "apps/android/app/src/main/java/com/iphoneremote/remote/RemoteSession.kt",
    "apps/android/app/src/main/java/com/iphoneremote/remote/ControlMessage.kt",
    "apps/android/app/build.gradle.kts",
    "apps/android/settings.gradle.kts",
    "apps/android/build.gradle.kts",

    # ── Android JVM unit tests ──
    "apps/android/app/src/test/java/com/iphoneremote/remote/ControlMessageTest.kt",

    # ── Android instrumentation tests ──
    "apps/android/app/src/androidTest/java/com/iphoneremote/remote/MainActivityInteractionTest.kt",

    # ── Infrastructure ──
    "infra/docker-compose.coturn.yml",

    # ── E2E / Maestro ──
    "e2e/maestro/android-login-and-session.yaml",
    "e2e/maestro/android-remote-touch-sim.yaml",
    "e2e/maestro/android-smoke.yaml",
    "e2e/maestro/android-session-disconnect.yaml",
    "e2e/scripts/run-android-e2e-on-device.ps1",
    "e2e/scripts/run-packaging-gate.ps1",
    "e2e/scripts/verify-project-files.ps1",
    "e2e/scripts/e2e-signaling-test.mjs",
    "e2e/scripts/package.json",

    # ── CI & root ──
    ".github/workflows/ci.yml",
    ".gitignore",
    "README.md"
)

Write-Host "`n== Verifying project files in $Root ==" -ForegroundColor Cyan
Write-Host "Searching for file list matching 'iphone-remote' project structure...`n" -ForegroundColor Gray

$missing = @()
$found   = 0

foreach ($rel in $expected) {
    $full = Join-Path $Root $rel
    if (Test-Path $full) {
        $found++
        Write-Host "  OK   $rel" -ForegroundColor Green
    } else {
        $missing += $rel
        Write-Host "  MISS $rel" -ForegroundColor Red
    }
}

Write-Host "`nResult: $found/$($expected.Count) files found." -ForegroundColor $(if ($missing.Count -eq 0) { "Green" } else { "Red" })

if ($missing.Count -gt 0) {
    Write-Host "`nMissing files:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "`nVerify failed — project file search for 'iphone-remote' incomplete." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nAll expected project files present — 'iphone-remote' project file search passed." -ForegroundColor Green
    exit 0
}