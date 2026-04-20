param(
    [string]$EnvFile = ".env.local"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repoRoot $EnvFile

if (-not (Test-Path $envPath)) {
    throw "Env file not found: $envPath"
}

Get-Content $envPath | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_) -or $_.Trim().StartsWith("#")) {
        return
    }

    $parts = $_.Split("=", 2)
    if ($parts.Length -eq 2) {
        [System.Environment]::SetEnvironmentVariable($parts[0], $parts[1], "Process")
    }
}

dotnet run --project "$repoRoot/backend/TuneFinder.Api/TuneFinder.Api.csproj"
