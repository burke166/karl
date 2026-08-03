<#
.SYNOPSIS
Packs every packable project in Karl.sln - Karl.Cli (a dotnet tool) plus the 8
Karl.* library packages - and pushes the resulting .nupkg files to a local or
UNC-path NuGet feed. Intended for the internal network feed; NuGet.org publishing
(including symbols) is handled separately by .github/workflows/publish-nuget.yml
on tag push.

.EXAMPLE
.\scripts\publish-local.ps1 -FeedPath \\fileserver\nuget
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$FeedPath,

    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'dotnet $($Arguments -join ' ')' failed with exit code $LASTEXITCODE"
    }
}

if (-not (Microsoft.PowerShell.Management\Test-Path $FeedPath)) {
    throw "Feed path '$FeedPath' does not exist or is not reachable."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "Karl.sln"
$outputPath = Join-Path $repoRoot "artifacts\nupkgs"

# Projects set GeneratePackageOnBuild=true, under which NuGet's Pack target no longer
# depends on Build (it assumes packing already happened via `dotnet build`). So pack alone
# fails with NU5026 ("file to be packed was not found") against clean obj/bin - build first,
# then pack --no-build to just collect the already-built outputs. Karl.Test sets
# IsPackable=false and is skipped by both steps automatically.
Invoke-Checked @("build", $solutionPath, "--configuration", $Configuration)
Invoke-Checked @("pack", $solutionPath, "--configuration", $Configuration, "--no-build", "--output", $outputPath)

# .snupkg is intentionally not pushed here: `dotnet nuget push` of a symbol package against a
# local/UNC folder source silently no-ops (exit 0, nothing copied, no error) - folder feeds don't
# have a symbol-server endpoint for it to land on. Symbols are only meaningful via the NuGet.org
# push in the GitHub Actions workflow.
$packages = Get-ChildItem -Path (Join-Path $outputPath "*") -Include "*.nupkg"
if (-not $packages) {
    throw "No .nupkg files found in '$outputPath' after packing."
}

foreach ($package in $packages) {
    Invoke-Checked @("nuget", "push", $package.FullName, "--source", $FeedPath, "--skip-duplicate")
}
