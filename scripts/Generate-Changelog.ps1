<#
.SYNOPSIS
    Regenerates the full changelog from git tags/commits and extracts the current version's section.

.DESCRIPTION
    Builds dist/CHANGELOG.md from scratch every run (newest tag first), one "## <tag> - <date>" section
    per v* tag with a bullet per non-merge commit subject since the previous tag. Nothing is committed
    back to the repo - the file is a build artifact, regenerated from git history each release so the
    pipeline stays reproducible without needing write-back permissions.

    Also writes dist/current-version-notes.md containing only the -Tag section, for use as the GitHub
    release body.

.NOTES
    Requires the full git history (checkout with fetch-depth: 0) - a shallow clone will only see one tag.
#>
[CmdletBinding()]
param(
    [string]$Tag = $env:GITHUB_REF_NAME,
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

if (-not $Tag) {
    $Tag = (git describe --tags --exact-match 2>$null)
    if (-not $Tag) {
        throw "No -Tag supplied and no exact tag match on HEAD - pass -Tag explicitly."
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$tagLines = git for-each-ref --sort=creatordate --format='%(refname:short)|%(creatordate:short)' refs/tags/v*
if (-not $tagLines) {
    throw "No tags matching 'v*' found - is this checkout shallow (needs fetch-depth: 0)?"
}

$tags = @()
foreach ($line in $tagLines) {
    $parts = $line -split '\|', 2
    $tags += [PSCustomObject]@{ Name = $parts[0]; Date = $parts[1] }
}

if (-not ($tags.Name -contains $Tag)) {
    throw "Tag '$Tag' not found among known tags ($($tags.Name -join ', ')) - checkout may be missing it."
}

$sections = New-Object System.Collections.Generic.List[string]

for ($i = $tags.Count - 1; $i -ge 0; $i--) {
    $current = $tags[$i]
    $range = if ($i -eq 0) { $current.Name } else { "$($tags[$i - 1].Name)..$($current.Name)" }

    $subjects = git log $range --no-merges --pretty=%s
    $bullets = if ($subjects) { ($subjects | ForEach-Object { "- $_" }) -join "`n" } else { "- _no changes recorded_" }

    $sections.Add("## $($current.Name) - $($current.Date)`n`n$bullets`n")
}

$changelog = ($sections -join "`n")
$changelogPath = Join-Path $OutputDir "CHANGELOG.md"
Set-Content -Path $changelogPath -Value $changelog -Encoding UTF8
Write-Host "Wrote $($tags.Count) version section(s) to $changelogPath"

$escapedTag = [regex]::Escape($Tag)
$match = [regex]::Match($changelog, "(?ms)^## $escapedTag\b.*?(?=^## |\z)")
if (-not $match.Success) {
    throw "Could not locate the '$Tag' section in the generated changelog."
}

$notesPath = Join-Path $OutputDir "current-version-notes.md"
Set-Content -Path $notesPath -Value $match.Value.TrimEnd() -Encoding UTF8
Write-Host "Wrote current-version release notes for '$Tag' to $notesPath"
