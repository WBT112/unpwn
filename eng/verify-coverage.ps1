param(
    [Parameter(Mandatory = $true)]
    [string] $CoveragePath,

    [ValidateRange(0.0, 1.0)]
    [double] $MinimumLineRate = 0.80,

    [ValidateRange(0.0, 1.0)]
    [double] $MinimumBranchRate = 0.80
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoveragePath -PathType Leaf)) {
    throw "Coverage report was not found at '$CoveragePath'."
}

[xml] $coverage = Get-Content -LiteralPath $CoveragePath -Raw
$root = $coverage.DocumentElement
if ($null -eq $root -or $root.LocalName -ne 'coverage') {
    throw "Coverage report '$CoveragePath' does not contain a Cobertura coverage root."
}

$lineRateValue = $root.GetAttribute('line-rate')
$branchRateValue = $root.GetAttribute('branch-rate')
if ([string]::IsNullOrWhiteSpace($lineRateValue) -or
    [string]::IsNullOrWhiteSpace($branchRateValue)) {
    throw "Coverage report '$CoveragePath' does not contain Cobertura line-rate and branch-rate values."
}

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$lineRate = [double]::Parse($lineRateValue, $culture)
$branchRate = [double]::Parse($branchRateValue, $culture)
$linePercent = $lineRate.ToString('P2', $culture)
$branchPercent = $branchRate.ToString('P2', $culture)

Write-Host "Coverage: $linePercent LOC, $branchPercent branches."

$failures = @()
if ($lineRate -lt $MinimumLineRate) {
    $failures += "LOC coverage $linePercent is below $($MinimumLineRate.ToString('P2', $culture))."
}

if ($branchRate -lt $MinimumBranchRate) {
    $failures += "Branch coverage $branchPercent is below $($MinimumBranchRate.ToString('P2', $culture))."
}

if ($failures.Count -gt 0) {
    throw ($failures -join ' ')
}
