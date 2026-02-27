param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$OutFile
)

# Exclude custom themes that contain third-party copyrighted assets.
# The Doom theme ships only in the source repo (see CustomThemes/Doom/ATTRIBUTION.md).
$excludedDirs = @('CustomThemes\Doom')

$files = Get-ChildItem -Path $PublishDir -File -Recurse |
    Where-Object {
        $rel = [System.IO.Path]::GetRelativePath($PublishDir, $_.FullName)
        -not ($excludedDirs | Where-Object { $rel.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase) })
    } |
    Sort-Object FullName

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="PublishFiles" Directory="INSTALLFOLDER">')

foreach ($file in $files) {
    $rel = [System.IO.Path]::GetRelativePath($PublishDir, $file.FullName)
    $subdir = [System.IO.Path]::GetDirectoryName($rel)
    $source = "$PublishDir\$rel"

    if ($subdir) {
        [void]$sb.AppendLine("      <Component Subdirectory=`"$subdir`">")
    } else {
        [void]$sb.AppendLine("      <Component>")
    }
    [void]$sb.AppendLine("        <File Source=`"$source`" />")
    [void]$sb.AppendLine("      </Component>")
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$sb.ToString() | Set-Content -Path $OutFile -Encoding UTF8
Write-Host "Generated $OutFile with $($files.Count) files."
