param(
    [string]$Root = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$rootPath = [System.IO.Path]::GetFullPath($Root)
$sourceRoot = Join-Path $rootPath 'src'

$approvedUnsafeProjects = @(
    'src/Unpwn.App/Unpwn.App.csproj'
)

$approvedNativeSources = @(
    'src/Unpwn.App/Services/RecoveryBrowserPlatformAdapter.cs',
    'src/Unpwn.App/Services/LinuxGtkWebViewInitializationScope.cs'
)

function Get-RelativeRepositoryPath([string]$Path) {
    return [System.IO.Path]::GetRelativePath($rootPath, $Path).Replace('\', '/')
}

$violations = [System.Collections.Generic.List[string]]::new()

Get-ChildItem $sourceRoot -Recurse -File |
    Where-Object { $_.Extension -in '.csproj', '.props', '.targets' } |
    ForEach-Object {
        $relative = Get-RelativeRepositoryPath $_.FullName
        $content = Get-Content $_.FullName -Raw
        if ($content -match '<AllowUnsafeBlocks>\s*true\s*</AllowUnsafeBlocks>' -and
            $relative -notin $approvedUnsafeProjects) {
            $violations.Add("$relative enables AllowUnsafeBlocks outside the approved native boundary.")
        }
    }

$nativePatterns = [ordered]@{
    'P/Invoke declaration' = '(?m)\[\s*(?:LibraryImport|DllImport)\s*\('
    'unsafe declaration' = '(?m)\bunsafe\s+(?:(?:partial|static|sealed|readonly|ref)\s+)*(?:class|struct|record|interface|delegate|void|byte|sbyte|short|ushort|int|uint|long|ulong|char|float|double|nint|nuint)\b'
    'pointer declaration' = '(?m)\b(?:void|byte|sbyte|short|ushort|int|uint|long|ulong|char|float|double|nint|nuint)\s*\*'
    'raw memory API' = '\b(?:Buffer\.MemoryCopy|NativeMemory\.|MemoryMarshal\.|Unsafe\.|Marshal\.(?:Copy|PtrToStructure|StructureToPtr|AllocHGlobal|FreeHGlobal|ReadInt\d*|WriteInt\d*))'
}

Get-ChildItem $sourceRoot -Recurse -File -Filter '*.cs' |
    ForEach-Object {
        $relative = Get-RelativeRepositoryPath $_.FullName
        if ($relative -in $approvedNativeSources) {
            return
        }

        $content = Get-Content $_.FullName -Raw
        foreach ($entry in $nativePatterns.GetEnumerator()) {
            if ($content -match $entry.Value) {
                $violations.Add("$relative contains $($entry.Key) outside the approved native boundary.")
            }
        }
    }

if ($violations.Count -gt 0) {
    Write-Error ("Native/unsafe boundary verification failed:`n - " + ($violations -join "`n - "))
    exit 1
}

Write-Host 'Native/unsafe boundary verified.'
Write-Host 'Approved unsafe project: src/Unpwn.App/Unpwn.App.csproj'
Write-Host 'Approved native sources:'
$approvedNativeSources | ForEach-Object { Write-Host " - $_" }
