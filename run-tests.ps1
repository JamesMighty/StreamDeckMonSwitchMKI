# Runs all end-to-end test suites for StreamDeckMonSwitchMKI.
#
#   .\run-tests.ps1            # run everything
#   .\run-tests.ps1 -Suite backend
#   .\run-tests.ps1 -Suite frontend
#   .\run-tests.ps1 -Suite e2e
#
# Prerequisites:
#   - Backend suite: MSBuild (Visual Studio) + .NET SDK (dotnet test)
#   - Frontend suite: Node.js + npm (npm install in frontend/sdpi-components)
#   - E2E suite: Node.js + npm (npm install + npx playwright install in tests/e2e)

param(
    [ValidateSet('all', 'backend', 'frontend', 'e2e')]
    [string]$Suite = 'all'
)

# Note: keep ErrorActionPreference at Continue - native tools (msbuild, dotnet,
# npx) print warnings to stderr, which PS 5.1 would otherwise turn into
# terminating errors. Failures are detected via $LASTEXITCODE checks instead.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot

function Invoke-BackendTests {
    Write-Host "`n=== Backend e2e tests (NUnit, real plugin process) ===" -ForegroundColor Cyan

    # Locate MSBuild via vswhere.
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = $null
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
    if (-not $msbuild) {
        throw "MSBuild not found - install Visual Studio with the MSBuild component."
    }

    # The backend tests reference the built plugin exe, so build it first.
    & $msbuild "$root\backend\StreamDeckMonitorSwitch.csproj" /t:Build /p:Configuration=Debug /v:quiet /nologo
    if ($LASTEXITCODE -ne 0) { throw "Backend build failed" }

    dotnet test "$root\tests\StreamDeckMonSwitchMKI.Tests\StreamDeckMonSwitchMKI.Tests.csproj" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Backend tests failed" }
}

function Invoke-FrontendTests {
    Write-Host "`n=== Frontend e2e tests (Jest, mocked Stream Deck websocket) ===" -ForegroundColor Cyan

    Push-Location "$root\frontend\sdpi-components"
    try {
        if (-not (Test-Path node_modules)) {
            npm install --force
            if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
        }

        npx jest
        if ($LASTEXITCODE -ne 0) { throw "Frontend tests failed" }
    }
    finally {
        Pop-Location
    }
}

function Invoke-E2ETests {
    Write-Host "`n=== Playwright e2e tests (real browser + real plugin process) ===" -ForegroundColor Cyan

    # Locate MSBuild (needed to build the plugin exe the e2e suite launches).
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = $null
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
    if (-not $msbuild) {
        throw "MSBuild not found - install Visual Studio with the MSBuild component."
    }

    # Build the backend (Debug) - the e2e suite launches the real plugin exe.
    & $msbuild "$root\backend\StreamDeckMonitorSwitch.csproj" /t:Build /p:Configuration=Debug /v:quiet /nologo
    if ($LASTEXITCODE -ne 0) { throw "Backend build failed" }

    # Build the sdpi-components bundle the Property Inspector loads.
    Push-Location "$root\frontend\sdpi-components"
    try {
        if (-not (Test-Path node_modules)) {
            npm install --force
            if ($LASTEXITCODE -ne 0) { throw "npm install failed (sdpi-components)" }
        }
        npm run build_only
        if ($LASTEXITCODE -ne 0) { throw "sdpi-components build failed" }
    }
    finally {
        Pop-Location
    }

    # Run the Playwright suite.
    Push-Location "$root\tests\e2e"
    try {
        if (-not (Test-Path node_modules)) {
            npm install
            if ($LASTEXITCODE -ne 0) { throw "npm install failed (tests/e2e)" }
            npx playwright install chromium
            if ($LASTEXITCODE -ne 0) { throw "playwright install failed" }
        }
        npx playwright test
        if ($LASTEXITCODE -ne 0) { throw "Playwright e2e tests failed" }
    }
    finally {
        Pop-Location
    }
}

switch ($Suite) {
    'backend'  { Invoke-BackendTests }
    'frontend' { Invoke-FrontendTests }
    'e2e'      { Invoke-E2ETests }
    default    { Invoke-BackendTests; Invoke-FrontendTests; Invoke-E2ETests }
}

Write-Host "`nAll requested test suites passed." -ForegroundColor Green
