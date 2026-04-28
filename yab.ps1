param(
    [Parameter(ValueFromRemainingArguments = $true)]
    $RemainingArgs
)

# Ensure we use the correct path to the project
$projectPath = Join-Path $PSScriptRoot "Yab.Cli/Yab.Cli.csproj"

# Run the project with the passed arguments
dotnet run --project $projectPath -- $RemainingArgs
