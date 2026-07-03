$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$NativeDir = Join-Path $Root 'native\bin'
$NativeDll = Join-Path $NativeDir 'IcfCodec.dll'
$Project = Join-Path $Root 'IcfEditor\IcfEditor.csproj'
$Output = Join-Path $Root 'artifacts'

$Compiler = (Get-Command g++ -ErrorAction Stop).Source
New-Item -ItemType Directory -Force $NativeDir | Out-Null

& $Compiler -std=c++17 -O2 -s -shared -static-libgcc -static-libstdc++ `
  -o $NativeDll (Join-Path $Root 'native\IcfCodec.cpp') -lbcrypt
if ($LASTEXITCODE) { throw 'Native codec build failed.' }

dotnet publish $Project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $Output
if ($LASTEXITCODE) { throw 'Application build failed.' }

Get-FileHash (Join-Path $Output 'ICF-Editor.exe') -Algorithm SHA256
