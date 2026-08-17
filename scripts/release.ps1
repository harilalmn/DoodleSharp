<#
.SYNOPSIS
    Release driver for DoodleSharp. Stamps a calendar version, commits, tags, and pushes.

.DESCRIPTION
    DoodleSharp uses calendar versioning: YEAR.MONTH.PATCH. YEAR and MONTH are
    taken from today's date; PATCH counts releases within the same month and
    resets to 0 the first time you release in a new month or year. So the
    second release in May 2026 is 2026.5.1, and the first release in June is
    2026.6.0 — no -Bump argument to choose.

    The script writes the computed version into Directory.Build.props and
    installer.iss, commits the bump on main, tags it `v<version>`, and pushes
    the tag to origin. The `.github/workflows/release.yml` workflow takes over
    from there: it builds DoodleSharp (Release), runs Inno Setup, creates the
    GitHub release, and attaches the installer.

    Run /update-docs FIRST so the docs commit goes out before the bump
    commit — the release should ship with current documentation.

.PARAMETER LocalBuild
    Also build Release configs and the installer locally before pushing
    (useful for offline smoke-testing). The GitHub Actions workflow still
    builds and publishes the canonical artifacts on tag push.

.EXAMPLE
    .\scripts\release.ps1
    .\scripts\release.ps1 -LocalBuild
#>
param(
    [switch]$LocalBuild
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# Run git robustly. Under $ErrorActionPreference='Stop', PowerShell turns ANY
# native-command stderr output into a terminating NativeCommandError — even when
# the command exits 0. git routinely writes benign notices to stderr (the
# "LF will be replaced by CRLF" warning on `git add`, plus `fetch`/`push`
# progress), which previously aborted this script mid-release. This helper runs
# git with stderr merged into stdout (so nothing hits the error stream) under a
# local 'Continue' preference, and fails ONLY on a nonzero exit code. It returns
# just the real stdout lines so callers that parse output stay clean.
function Invoke-Git {
    $ErrorActionPreference = 'Continue'
    $out = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | ForEach-Object { Write-Host $_ }
        throw "git $($args -join ' ') failed (exit code $LASTEXITCODE)."
    }
    $out | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }
}

# UTF-8 round-tripping, both directions, explicitly.
#
# Windows PowerShell 5.1 decodes a BOM-less file using the system ANSI codepage and encodes with it
# too, so Get-Content/Set-Content silently turns every em-dash in CHANGELOG.md into mojibake. Both
# halves matter: reading with the wrong codepage corrupts the text before it is ever written, so a
# careful write alone would just persist the damage. This has already destroyed a source file in
# this repo once.
function Read-Utf8 {
    param([string]$Path)
    [System.IO.File]::ReadAllText((Resolve-Path $Path).Path, [System.Text.UTF8Encoding]::new($false))
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText((Resolve-Path $Path).Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

# Guard: clean working tree, on main, in sync with origin.
$status = Invoke-Git status --porcelain
if ($status) {
    Write-Error "Working tree not clean. Commit or stash first."
}
$branch = "$(Invoke-Git rev-parse --abbrev-ref HEAD)".Trim()
if ($branch -ne "main") {
    Write-Error "Not on main (currently $branch). Switch first."
}
Invoke-Git fetch origin --quiet
$behind = "$(Invoke-Git rev-list HEAD..origin/main --count)".Trim()
if ($behind -ne "0") {
    Write-Error "Local main is behind origin/main by $behind commit(s). Pull first."
}

# 1. Read current version from Directory.Build.props.
$propsPath = "Directory.Build.props"
[xml]$props = Get-Content $propsPath
$current = [Version]($props.Project.PropertyGroup.Version)

# 2. Compute the new calendar version: YEAR.MONTH.PATCH.
#    YEAR and MONTH come from today; PATCH increments within the same month
#    and resets to 0 the first time we release in a new month or year.
$now   = Get-Date
$year  = $now.Year
$month = $now.Month
if ($current.Major -eq $year -and $current.Minor -eq $month) {
    $patch = $current.Build + 1
} else {
    $patch = 0
}
$new = "$year.$month.$patch"
$newTag = "v$new"
Write-Host "Bumping $current -> $new" -ForegroundColor Cyan

# 3. Update Directory.Build.props.
$props.Project.PropertyGroup.Version = $new
$props.Project.PropertyGroup.AssemblyVersion = "$new.0"
$props.Project.PropertyGroup.FileVersion = "$new.0"
$props.Save((Resolve-Path $propsPath).Path)

# 4. Sync installer.iss MyAppVersion.
$iss = Read-Utf8 installer.iss
$iss = $iss -replace '(?m)^(#define MyAppVersion\s+").*?(")', "`${1}$new`${2}"
Write-Utf8NoBom -Path installer.iss -Content $iss

# 5. Stamp CHANGELOG.md: turn the [Unreleased] section into this version.
#
#    Without this the script bumped two version files and left the changelog alone, so every
#    release's entries stayed under [Unreleased] and the curated history silently fell a release
#    behind. Caught when 2026.8.5's entries were still sitting there after it had shipped.
$changelogPath = "CHANGELOG.md"
if (Test-Path $changelogPath) {
    $changelog = Read-Utf8 $changelogPath

    # Everything between "## [Unreleased]" and the next "## [" heading.
    $unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\]\s*(.*?)(?=^## \[)')

    if (-not $unreleased.Success) {
        Write-Warning "CHANGELOG.md has no [Unreleased] section to stamp - leaving it alone."
    }
    elseif ([string]::IsNullOrWhiteSpace($unreleased.Groups[1].Value)) {
        # A release with no curated notes is worth noticing, but not worth blocking on: the
        # GitHub release body is generated from the commit log either way.
        Write-Warning "CHANGELOG.md [Unreleased] is empty - releasing $new with no curated notes."
    }
    else {
        $today = Get-Date -Format 'yyyy-MM-dd'
        # Capture the heading's own line ending and reuse it, so stamping a CRLF file does not
        # leave one stray LF line behind.
        $changelog = $changelog -replace '(?m)^(## \[Unreleased\])[ \t]*(\r?\n)',
                                         "`${1}`${2}`${2}## [$new] - $today`${2}"
        Write-Utf8NoBom -Path $changelogPath -Content $changelog
        Write-Host "Stamped CHANGELOG.md section [$new] - $today" -ForegroundColor Cyan
        $changelogChanged = $true
    }
}

# 6. Commit the bump.
Invoke-Git add Directory.Build.props installer.iss
if ($changelogChanged) { Invoke-Git add CHANGELOG.md }
Invoke-Git commit -m "Release $newTag"

# 6. Optional local smoke build.
if ($LocalBuild) {
    # Merge stderr into stdout (2>&1) before discarding, so build-tool stderr
    # output doesn't trip the same Stop-mode NativeCommandError; gate on exit code.
    Write-Host "Building DoodleSharp (Release)..." -ForegroundColor Cyan
    & dotnet build DoodleSharp.csproj -c Release -nologo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "DoodleSharp build failed." }

    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $iscc) {
        Write-Host "Building installer..." -ForegroundColor Cyan
        & $iscc installer.iss 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup build failed." }
    } else {
        Write-Warning "ISCC.exe not found at $iscc; skipping local installer build (CI will still build it)."
    }
}

# 7. Tag and push. The tag push triggers .github/workflows/release.yml,
#    which builds Release configs, runs Inno Setup, and publishes the
#    GitHub release with the installer attached.
Invoke-Git tag $newTag
Invoke-Git push origin main
Invoke-Git push origin $newTag
Write-Host "Pushed $newTag to origin." -ForegroundColor Green

Write-Host ""
Write-Host "Release workflow triggered. Watch progress at:" -ForegroundColor Cyan
Write-Host "  https://github.com/harilalmn/DoodleSharp/actions/workflows/release.yml"
Write-Host ""
Write-Host "When green, the release will appear at:" -ForegroundColor Cyan
Write-Host "  https://github.com/harilalmn/DoodleSharp/releases/tag/$newTag"
