<#
.SYNOPSIS
    Ships what is on GitHub's develop by fast-forwarding main to it.

.DESCRIPTION
    A push to main deploys the API and releases the client. This pushes the commit origin/develop
    points at, and only once CI has passed for it on develop. Git refuses anything but a fast-forward,
    so main never loses commits.

    Asks before pushing. -Confirm:$false skips the question; -WhatIf shows what would ship and stops.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $args failed."
    }
    return $output
}

function Stop-Ship([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

$remote = Invoke-Git remote get-url origin
if ($remote -notmatch 'github\.com[:/](?<repository>[^/]+/[^/]+?)(\.git)?$') {
    Stop-Ship "origin is not a GitHub repository: $remote"
}
$repository = $Matches.repository

Invoke-Git fetch --quiet origin
$develop = Invoke-Git rev-parse origin/develop
$main = Invoke-Git rev-parse origin/main
$short = Invoke-Git rev-parse --short $develop

if ($develop -eq $main) {
    Write-Host 'Nothing to ship. main is already at develop.'
    exit 0
}

& git merge-base --is-ancestor $main $develop
if ($LASTEXITCODE -ne 0) {
    Stop-Ship "main has commits that develop doesn't. Bring them into develop first."
}

& git rev-parse --verify --quiet refs/heads/develop | Out-Null
if ($LASTEXITCODE -eq 0) {
    $unpushed = Invoke-Git rev-list --count origin/develop..develop
    if ([int]$unpushed -gt 0) {
        Write-Host "Local develop has $unpushed commits that aren't on GitHub. They won't ship." -ForegroundColor Yellow
    }
}

$commits = @(Invoke-Git log --oneline --no-decorate "$main..$develop")
$migrations = @(
    Invoke-Git diff --name-only --diff-filter=A $main $develop -- 'ModsDude.Server/ModsDude.Server.Persistence/Migrations/' |
        Where-Object { $_ -notmatch '(\.Designer|ModelSnapshot)\.cs$' } |
        ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) })

Write-Host "Commits ($($commits.Count)):"
$commits | ForEach-Object { Write-Host "  $_" }

if ($migrations.Count -gt 0) {
    Write-Host "Migrations ($($migrations.Count)):" -ForegroundColor Yellow
    $migrations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

$runs = Invoke-RestMethod `
    -Uri "https://api.github.com/repos/$repository/actions/workflows/ci.yml/runs?head_sha=$develop&branch=develop&event=push" `
    -Headers @{ Accept = 'application/vnd.github+json' }
$run = $runs.workflow_runs | Sort-Object run_number -Descending | Select-Object -First 1

if ($null -eq $run) {
    Stop-Ship "CI hasn't run for $short on develop."
}
if ($run.status -ne 'completed') {
    Stop-Ship "CI for $short is still $($run.status -replace '_', ' '): $($run.html_url)"
}
if ($run.conclusion -ne 'success') {
    Stop-Ship "CI for $short ended in $($run.conclusion): $($run.html_url)"
}

Write-Host "CI passed for $short."

if (-not $PSCmdlet.ShouldProcess('main', "Ship $short")) {
    exit 0
}

Invoke-Git push origin "${develop}:refs/heads/main"

Write-Host "Shipped $short. Deploy: https://github.com/$repository/actions/workflows/ci.yml?query=branch%3Amain" -ForegroundColor Green
