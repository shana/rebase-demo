$topDirectory = Split-Path (Split-Path $MyInvocation.MyCommand.Path)
$thisDirectory = Split-Path $MyInvocation.MyCommand.Path
$nuget = Join-Path $topDirectory "nuget\nuget.exe"
& $nuget restore (Join-Path $thisDirectory TrackingCollection.sln) -NonInteractive -Verbosity detailed
