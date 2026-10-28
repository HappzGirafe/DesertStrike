# Publishes the second Low Strike website, https://happzgirafe.github.io/ (the GitHub Pages user site: the repository
# HappzGirafe/HappzGirafe.github.io, served from its main branch). That repository is only the output; nothing there is
# edited by hand. It is put together from this repository:
#   GitHubIO\                     the page itself (index.html, 404.html, robots.txt, sitemap.xml, README.md) and the
#                                 pictures only it uses (GitHubIO\media)
#   docs\play                     the browser version, the same build as lowstrike.lokvetia.com/play/
#   docs\media                    the video and the other pictures
#   docs\online.json, favicon.png where the page finds the online server, and the icon
# Run it after changing GitHubIO\, and after every new browser build (docs\play), so both sites offer the same game.
# Needs git, logged in to GitHub as HappzGirafe. -DryRun puts the site together and shows what would change, without
# publishing. GitHub Pages serves a push about a minute later.
#
#   powershell -ExecutionPolicy Bypass -File Tools\publish_github_io.ps1 [-DryRun] [-CoAuthor "Name <email>"]
param([switch]$DryRun, [string]$CoAuthor)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$site = "HappzGirafe/HappzGirafe.github.io"
$work = Join-Path $env:TEMP "LowStrike-github.io"

# (Not named "Git": PowerShell names ignore case, so it would call itself instead of git.exe.) Git's notes on stderr are
# not errors: only its exit code counts. The files go up as they are, without line-end conversion.
function SiteGit {
    $ErrorActionPreference = "Continue"
    git.exe -C $work -c core.autocrlf=false @args
    if ($LASTEXITCODE -ne 0) { throw "git $args failed" }
}
function Mirror($from, $to) {
    # robocopy: 0-7 mean success (1 = files copied); /MIR also removes what is no longer in $from.
    robocopy (Join-Path $repo $from) (Join-Path $work $to) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $from failed (robocopy $LASTEXITCODE)" }
}

if (-not (Test-Path (Join-Path $repo "docs\play\Build\WebGL.loader.js"))) { throw "No browser build in docs\play." }
if (Test-Path (Join-Path $work ".git")) {
    # Start from what is published (a new, empty repository has nothing to fetch yet).
    if (SiteGit ls-remote --heads origin main) {
        SiteGit fetch --depth 1 origin main
        SiteGit checkout -f -B main FETCH_HEAD
        SiteGit clean -fdq
    }
} else {
    $ErrorActionPreference = "Continue"
    git.exe clone --depth 1 "https://github.com/$site.git" $work
    if ($LASTEXITCODE -ne 0) { throw "Could not clone $site (does the repository exist?)" }
    $ErrorActionPreference = "Stop"
}

Mirror "docs\play" "play"
Mirror "docs\media" "media"
Get-ChildItem (Join-Path $repo "GitHubIO\media") | Copy-Item -Destination (Join-Path $work "media")
foreach ($file in "index.html", "404.html", "robots.txt", "sitemap.xml", "README.md") {
    Copy-Item (Join-Path $repo "GitHubIO\$file") -Destination $work
}
Copy-Item (Join-Path $repo "docs\online.json"), (Join-Path $repo "docs\favicon.png") -Destination $work
# No Jekyll: the files are served as they are (and the build is faster).
Set-Content -Path (Join-Path $work ".nojekyll") -Value "" -Encoding ASCII -NoNewline

SiteGit add -A
$changes = SiteGit status --porcelain
if (-not $changes) { "Nothing changed: https://happzgirafe.github.io/ is up to date."; return }
"Changes:"; $changes
if ($DryRun) { "Dry run: nothing published. The site is put together in $work"; return }

$commit = git.exe -C $repo rev-parse --short HEAD
$message = @("-m", "Website from DesertStrike $commit")
if ($CoAuthor) { $message += @("-m", "Co-Authored-By: $CoAuthor") }
# Committed as the same person as this repository's commits (this PC may have no global git name).
$name = git.exe -C $repo config user.name
$email = git.exe -C $repo config user.email
SiteGit -c "user.name=$name" -c "user.email=$email" commit @message
SiteGit branch -M main
SiteGit push -u origin main
"Published: https://happzgirafe.github.io/ (GitHub Pages takes about a minute)"
