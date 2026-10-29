<#
.SYNOPSIS
    Renders the generated CHANGELOG.md (full version history) as a standalone, Trainer-styled HTML page.

.DESCRIPTION
    Only understands the markdown subset Generate-Changelog.ps1 actually produces: "## <tag> - <date>"
    headings and "- <text>" bullets. The Trainer logo is embedded as base64 so the resulting file works
    standalone once unzipped, with no external references.
#>
[CmdletBinding()]
param(
    [string]$InputPath = "dist/CHANGELOG.md",
    [string]$OutputPath = "dist/Changelog.html",
    [string]$LogoPath = "Resources/Trainer-Logo.png",
    [string]$Title = "A2G-Trainer-XP - Changelog"
)

$ErrorActionPreference = "Stop"

function ConvertTo-HtmlEncoded([string]$text) {
    [System.Net.WebUtility]::HtmlEncode($text)
}

function Format-InlineMarkdown([string]$text) {
    $encoded = ConvertTo-HtmlEncoded $text
    [regex]::Replace($encoded, '_([^_]+)_', '<em>$1</em>')
}

$lines = Get-Content -Path $InputPath -Encoding UTF8
$body = New-Object System.Text.StringBuilder
$inList = $false

foreach ($line in $lines) {
    if ($line -match '^##\s+(?<tag>\S+)\s*-\s*(?<date>.+)$') {
        if ($inList) { [void]$body.AppendLine("  </ul>"); $inList = $false }
        $tag = ConvertTo-HtmlEncoded $Matches['tag']
        $date = ConvertTo-HtmlEncoded $Matches['date']
        [void]$body.AppendLine("  <section class=`"version`">")
        [void]$body.AppendLine("    <h2><span class=`"tag`">$tag</span><span class=`"date`">$date</span></h2>")
        continue
    }

    if ($line -match '^-\s+(.+)$') {
        if (-not $inList) { [void]$body.AppendLine("    <ul>"); $inList = $true }
        [void]$body.AppendLine("      <li>$(Format-InlineMarkdown $Matches[1])</li>")
        continue
    }

    if ([string]::IsNullOrWhiteSpace($line)) {
        if ($inList) { [void]$body.AppendLine("    </ul>"); $inList = $false; [void]$body.AppendLine("  </section>") }
        continue
    }
}
if ($inList) { [void]$body.AppendLine("    </ul>"); [void]$body.AppendLine("  </section>") }

$logoBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($LogoPath))
$encodedTitle = ConvertTo-HtmlEncoded $Title

$html = @"
<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<title>$encodedTitle</title>
<style>
  * { box-sizing: border-box; }
  body {
    margin: 0;
    padding: 40px 16px;
    min-height: 100vh;
    font-family: "Segoe UI", "Segoe UI Emoji", Tahoma, Geneva, sans-serif;
    background: linear-gradient(160deg, #f7941e 0%, #ffb347 45%, #ffd98a 100%);
    display: flex;
    justify-content: center;
  }
  .card {
    width: 100%;
    max-width: 760px;
    background: #ffffff;
    border-radius: 14px;
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.25);
    padding: 32px 40px 40px;
  }
  .header {
    display: flex;
    align-items: center;
    gap: 16px;
    border-bottom: 1px solid #f0e0c8;
    padding-bottom: 20px;
    margin-bottom: 12px;
  }
  .header img { width: 56px; height: 56px; }
  .header h1 { font-size: 22px; margin: 0; color: #5a3a10; }
  .header p { margin: 2px 0 0; color: #9a7a4a; font-size: 13px; }
  section.version { padding: 18px 0; border-bottom: 1px solid #f2ecdf; }
  section.version:last-child { border-bottom: none; }
  h2 { margin: 0 0 10px; font-size: 17px; display: flex; align-items: baseline; gap: 10px; }
  h2 .tag { color: #0078d4; font-weight: 700; }
  h2 .date { color: #9a9a9a; font-weight: 400; font-size: 13px; }
  ul { margin: 0; padding-left: 22px; }
  li { color: #333; line-height: 1.55; font-size: 14.5px; }
  li em { color: #9a7a4a; }
</style>
</head>
<body>
  <div class="card">
    <div class="header">
      <img src="data:image/png;base64,$logoBase64" alt="A2G-Trainer-XP">
      <div>
        <h1>A2G-Trainer-XP</h1>
        <p>Changelog</p>
      </div>
    </div>
$($body.ToString())  </div>
</body>
</html>
"@

Set-Content -Path $OutputPath -Value $html -Encoding UTF8
Write-Host "Wrote $OutputPath"
