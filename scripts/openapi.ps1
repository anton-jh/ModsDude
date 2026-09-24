<#
.SYNOPSIS
    Regenerates the checked-in OpenAPI document, or verifies that it still matches the server.

.DESCRIPTION
    The document is checked in so that a server change which the generated client has not caught up
    with shows as a diff rather than as nothing at all. Run with -Update after changing anything the
    document describes — a route, a DTO field, a problem type, a status code — and commit the result
    alongside the change. CI runs it without -Update, which fails the build when the two disagree.

    Nothing is started. The build writes the document from the assembly
    (Microsoft.Extensions.ApiDescription.Server), running the app's entry point against a server that
    never listens; Program.cs recognises that and skips everything that reaches outside the process -
    the database, storage and Hangfire. So this needs no database and cannot touch real data. The build
    goes to a directory of its own, so an API already running from the usual output does not lock it.

    The document is rewritten into a canonical form before it is written or compared — object keys in
    ordinal order, two-space indentation, LF line endings, no BOM — so that the file records what the
    API says rather than which machine asked it.

    Generated.cs is regenerated from the checked-in document afterwards, not from a server:
    'nswag run nswag-config.nswag' in ModsDude.Client.Core.

.PARAMETER Update
    Write the document instead of verifying it.
#>
[CmdletBinding()]
param(
    [switch]$Update,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'ModsDude.Server/ModsDude.Server.Api/ModsDude.Server.Api.csproj'
$documentPath = Join-Path $repositoryRoot 'openapi/v1.json'


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

# A directory of its own for everything the build writes: an API already running from the usual
# output holds its assemblies open, and the document itself belongs to this run alone.
$workPath = Join-Path ([System.IO.Path]::GetTempPath()) "modsdude-openapi-$([System.Guid]::NewGuid().ToString('N'))"
$outputPath = Join-Path $workPath 'bin'
$generatedPath = Join-Path $workPath 'document'

try {
    # Development because that is where the storage account is named. Storage is never called while
    # the document is written, but its client is still registered, and has to be: the registrations are
    # how the document tells a service parameter from a request body.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    Write-Host "Building $projectPath and writing its document ..."
    dotnet build $projectPath --configuration $Configuration --nologo --output $outputPath `
        -p:OpenApiGenerateDocumentsOnBuild=true "-p:OpenApiDocumentsDirectory=$generatedPath"
    if ($LASTEXITCODE -ne 0) {
        throw 'The API did not build, or its document could not be written.'
    }

    $generated = @(Get-ChildItem -Path $generatedPath -Filter '*.json')
    if ($generated.Count -ne 1) {
        throw "Expected the build to write one document to $generatedPath, found $($generated.Count)."
    }

    $canonical = ConvertTo-CanonicalJson ([System.IO.File]::ReadAllText($generated[0].FullName))
}
finally {
    Remove-Item -Recurse -Force -Path $workPath -ErrorAction SilentlyContinue
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
    Write-Host "$documentPath matches the API."
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
ModsDude.Client.Core's Generated.cs from it (nswag run nswag-config.nswag), and commit both.
The document the build wrote was copied to $actualPath.
"@
exit 1
