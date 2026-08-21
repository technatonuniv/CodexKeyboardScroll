[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.8 C# compiler was not found.'
}

$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $projectRoot $OutputDirectory
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$executable = Join-Path $outputPath 'CodexKeyboardScroll.exe'

# Keep the source list aligned with the project file so local and CI builds use
# the same compilation units without requiring a separate .NET SDK install.
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'CodexKeyboardScroll.csproj')
$namespace = New-Object Xml.XmlNamespaceManager($project.NameTable)
$namespace.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')
$sources = $project.SelectNodes('//msb:Compile', $namespace) | ForEach-Object {
    Join-Path $projectRoot $_.Include
}
$languageResources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Localization\Resources') -Filter '*.lang' |
    Sort-Object Name |
    ForEach-Object {
        "/resource:$($_.FullName),CodexKeyboardScroll.Localization.Resources.$($_.Name)"
    }
$iconResources = @(
    "/resource:$(Join-Path $projectRoot 'Assets\CodexKeyboardScroll.ico'),CodexKeyboardScroll.Assets.Active.ico",
    "/resource:$(Join-Path $projectRoot 'Assets\CodexKeyboardScroll.Waiting.ico'),CodexKeyboardScroll.Assets.Waiting.ico"
)

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:x64',
    '/warn:4',
    '/warnaserror+',
    "/out:$executable",
    "/win32icon:$(Join-Path $projectRoot 'Assets\CodexKeyboardScroll.ico')",
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')",
    "/reference:$(Join-Path $framework 'System.dll')",
    "/reference:$(Join-Path $framework 'System.Core.dll')",
    "/reference:$(Join-Path $framework 'System.Drawing.dll')",
    "/reference:$(Join-Path $framework 'System.Net.Http.dll')",
    "/reference:$(Join-Path $framework 'System.Runtime.Serialization.dll')",
    "/reference:$(Join-Path $framework 'System.Windows.Forms.dll')",
    "/reference:$(Join-Path $wpf 'UIAutomationClient.dll')",
    "/reference:$(Join-Path $wpf 'UIAutomationTypes.dll')",
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')"
) + $languageResources + $iconResources + $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$test = Start-Process -FilePath $executable -ArgumentList '--self-test' -Wait -PassThru
if ($test.ExitCode -ne 0) {
    throw "Self-tests reported $($test.ExitCode) failure(s)."
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $executable).Hash
Write-Host "Built: $executable"
Write-Host 'Self-tests: passed'
Write-Host "SHA-256: $hash"
