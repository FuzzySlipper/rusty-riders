# Pulls the ignored content\art\ into a Windows checkout (scripts/art-sync.sh says what it holds). The default
# source is den-agents' checkout through the P: share; -Store names another. Adds and updates, deletes nothing;
# -Mirror also deletes what the source lacks.
#
#   powershell -File scripts\art-sync.ps1
#   powershell -File scripts\art-sync.ps1 -Store \\server\share\rusty-riders\art
param(
    [string]$Store = 'P:\dev\rusty-riders\content\art',
    [switch]$Mirror
)
$ErrorActionPreference = 'Stop'
$art = Join-Path (Split-Path -Parent $PSScriptRoot) 'content\art'
if (-not (Test-Path $Store)) { throw "art-sync: $Store is not reachable" }
$mode = if ($Mirror) { '/MIR' } else { '/E' }
robocopy $Store $art $mode /FFT /NP /NFL /NDL /R:2 /W:2
if ($LASTEXITCODE -ge 8) { throw "art-sync: robocopy failed ($LASTEXITCODE)" }
exit 0
