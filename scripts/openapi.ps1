<#
.SYNOPSIS
    Regenerates the checked-in OpenAPI document, or verifies that it still matches the server.

.DESCRIPTION
    The document is checked in so that a server change which the generated client has not caught up
    with shows as a diff rather than as nothing at all. Run with -Update after changing anything the
    document describes — a route, a DTO field, a problem type, a status code — and commit the result
    alongside the change. CI runs it without -Update, which fails the build when the two disagree.

    The API has to be running to emit the document, and it migrates a database at startup, so this
    builds it, starts it against whatever ConnectionStrings__Database says, waits for the document to
    answer, and stops it again.

    The document is rewritten into a canonical form before it is written or compared — object keys in
    ordinal order, two-space indentation, LF line endings, no BOM — so that the file records what the
    API says rather than which machine asked it.

.PARAMETER Update
    Write the document instead of verifying it.

.PARAMETER UseRunningServer
    Do not build or start anything; fetch from an API that is already running at -Url.
#>
[CmdletBinding()]
param(
    [switch]$Update,
    [switch]$UseRunningServer,
    [string]$Url = 'http://localhost:5267',
    [string]$Configuration = 'Debug',
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'ModsDude.Server/ModsDude.Server.Api/ModsDude.Server.Api.csproj'
$documentPath = Join-Path $repositoryRoot 'openapi/v1.json'
$documentUrl = "$($Url.TrimEnd('/'))/swagger/v1/swagger.json"


function ConvertTo-CanonicalJson([string]$json) {
    $options = [System.Text.Json.JsonWriterOptions]::new()
    $options.Indented = $true
    # Pinned rather than left to the default: the escaping rule decides the bytes of every string in
    # the file, and a default that shifted between runtimes would read as drift in the API.
    $options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.Text.Json.Utf8JsonWriter]::new($stream, $options)
    $document = [System.Text.Json.JsonDocument]::Parse($json)

    try {
        Write-CanonicalElement -Element $document.RootElement -Writer $writer
        $writer.Flush()

        $text = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())

        return ($text -replace "`r`n", "`n") + "`n"
    }
    finally {
        $document.Dispose()
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Write-CanonicalElement($Element, $Writer) {
    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $Writer.WriteStartObject()

            # Ordinal rather than the shell's culture, and sorted at all because the document's own
            # ordering follows reflection and metadata order, which promises nothing.
            $names = [System.Collections.Generic.List[string]]::new()
            foreach ($property in $Element.EnumerateObject()) {
                $names.Add($property.Name)
            }
            $names.Sort([System.StringComparer]::Ordinal)

            foreach ($name in $names) {
                $Writer.WritePropertyName($name)
                Write-CanonicalElement -Element $Element.GetProperty($name) -Writer $Writer
            }

            $Writer.WriteEndObject()
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            $Writer.WriteStartArray()
            foreach ($item in $Element.EnumerateArray()) {
                Write-CanonicalElement -Element $item -Writer $Writer
            }
            $Writer.WriteEndArray()
        }
        default {
            $Element.WriteTo($Writer)
        }
    }
}

function Get-Document([string]$address, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        try {
            return (Invoke-WebRequest -Uri $address -UseBasicParsing -TimeoutSec 10).Content
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    throw "The API did not serve '$address' within $timeoutSeconds seconds."
}


$server = $null

try {
    if (-not $UseRunningServer) {
        Write-Host "Building $projectPath ..."
        dotnet build $projectPath --configuration $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            throw 'The API did not build.'
        }

        # The built assembly rather than 'dotnet run': run launches the app as a child process, and
        # stopping the launcher would leave the API listening.
        $assemblyPath = Join-Path $repositoryRoot "ModsDude.Server/ModsDude.Server.Api/bin/$Configuration/net10.0/ModsDude.Server.Api.dll"

        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = $Url

        Write-Host "Starting the API at $Url ..."
        # Started in the output directory because the content root defaults to the working directory,
        # and that is where the appsettings files were copied to. Environment variables still win over
        # them, which is how CI points the API at its own database.
        $server = Start-Process -FilePath 'dotnet' -ArgumentList $assemblyPath -PassThru -NoNewWindow `
            -WorkingDirectory (Split-Path -Parent $assemblyPath)
    }

    $canonical = ConvertTo-CanonicalJson (Get-Document $documentUrl $TimeoutSeconds)
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
    }
}

if ($Update) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $documentPath) | Out-Null
    [System.IO.File]::WriteAllText($documentPath, $canonical, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Wrote $documentPath"
    exit 0
}

if (-not (Test-Path $documentPath)) {
    Write-Error "$documentPath does not exist. Run this script with -Update and commit the result."
    exit 1
}

# Normalized on the way in as well as out. .gitattributes keeps the file LF, but a checkout that
# ignored it would otherwise report drift in every line of a document that had not changed at all.
$checkedIn = [System.IO.File]::ReadAllText($documentPath) -replace "`r`n", "`n"

if ($checkedIn -ceq $canonical) {
    Write-Host "$documentPath matches the running API."
    exit 0
}

# Written out so that the failure names what changed rather than only that something did.
$actualPath = Join-Path ([System.IO.Path]::GetTempPath()) 'modsdude-openapi-actual.json'
[System.IO.File]::WriteAllText($actualPath, $canonical, [System.Text.UTF8Encoding]::new($false))

Write-Host '--- Differences ---'
Compare-Object ($checkedIn -split "`n") ($canonical -split "`n") | Select-Object -First 60 | Format-Table -AutoSize | Out-String | Write-Host

Write-Error @"
The OpenAPI document does not match the API. The server has changed and the checked-in document — and
therefore the generated client — has not caught up. Run 'pwsh scripts/openapi.ps1 -Update', regenerate
ModsDude.Client.Core's Generated.cs against the API, and commit both.
The document the API served was written to $actualPath.
"@
exit 1
