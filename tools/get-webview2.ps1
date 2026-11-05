# Downloads Microsoft's WebView2 SDK from NuGet (once) and lays the files out next to the launcher:
#   webview2\Microsoft.Web.WebView2.Core.dll, webview2\Microsoft.Web.WebView2.WinForms.dll
#   webview2\x64\WebView2Loader.dll, webview2\x86\WebView2Loader.dll, webview2\arm64\WebView2Loader.dll
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "webview2"
if (Test-Path (Join-Path $out "Microsoft.Web.WebView2.WinForms.dll")) { Write-Host "WebView2 SDK already present"; exit 0 }
$tmp = Join-Path $env:TEMP ("wv2_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tmp | Out-Null
$url = "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2" + $(if ($Version) { "/$Version" } else { "" })
Write-Host "Downloading $url"
Invoke-WebRequest $url -OutFile (Join-Path $tmp "wv2.zip") -UseBasicParsing
Expand-Archive (Join-Path $tmp "wv2.zip") (Join-Path $tmp "pkg") -Force
$lib = Get-ChildItem (Join-Path $tmp "pkg\lib") -Directory | Where-Object { $_.Name -like "net4*" } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $lib) { throw "No .NET Framework assemblies found in the WebView2 package" }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $lib.FullName "Microsoft.Web.WebView2.Core.dll") $out
Copy-Item (Join-Path $lib.FullName "Microsoft.Web.WebView2.WinForms.dll") $out
foreach ($a in "x64","x86","arm64") {
  $src = Join-Path $tmp "pkg\runtimes\win-$a\native\WebView2Loader.dll"
  if (Test-Path $src) { New-Item -ItemType Directory -Force (Join-Path $out $a) | Out-Null; Copy-Item $src (Join-Path $out $a) }
}
$nuspec = Get-ChildItem (Join-Path $tmp "pkg") -Filter *.nuspec | Select-Object -First 1
if ($nuspec) { ([xml](Get-Content $nuspec.FullName)).package.metadata.version | Out-File (Join-Path $out "version.txt") -Encoding ascii }
Remove-Item $tmp -Recurse -Force
Write-Host "WebView2 SDK ready in $out (lib $($lib.Name))"
