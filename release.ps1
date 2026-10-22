# Builds the mod, tags the version from Plugin.cs and publishes a GitHub release with the DLL.
#
#   powershell -ExecutionPolicy Bypass -File release.ps1
#
# Bump the version in Plugin.cs and commit first. Close the game: the build also installs the DLL.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Native commands don't throw on failure, so check each one.
function Assert-Success($step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed." }
}

$project = (Get-ChildItem *.csproj).BaseName
$version = Select-String -Path Plugin.cs -Pattern 'BepInPlugin\([^,]+,\s*"[^"]*",\s*"([^"]+)"'
if (-not $version) { throw 'No version found in [BepInPlugin] in Plugin.cs.' }
$tag = 'v' + $version.Matches[0].Groups[1].Value

if (git status --porcelain) { throw 'There are uncommitted changes. Commit them first.' }
if (git tag --list $tag) { throw "$tag already exists. Bump the version in Plugin.cs." }

dotnet build -c Release
Assert-Success 'The build'

git tag -m "$project $tag" $tag
Assert-Success 'Tagging'

git push origin HEAD $tag
Assert-Success 'Pushing'

gh release create $tag "bin\Release\net6.0\$project.dll" --title $tag --generate-notes
Assert-Success 'Creating the release'

Write-Host "Released $project $tag"
