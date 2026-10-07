param([string]$MsysRoot = "$PSScriptRoot\..\.tools\msys64")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$MsysRoot = [IO.Path]::GetFullPath($MsysRoot)
$bash = Join-Path $MsysRoot 'usr\bin\bash.exe'
if (-not (Test-Path -LiteralPath $bash)) { throw 'Instale/prepare MSYS2 UCRT64 conforme README. Este script não instala MSYS2 nem serviços.' }
if ($MsysRoot -ne (Join-Path $root '.tools\msys64')) { throw 'Este build portátil usa .tools\msys64, conforme airplay.json.' }
Write-Host 'Build local UxPlay 1.73.7: downloads oficiais, SDK DNS-SD e executável somente na pasta do projeto. Bonjour Service NÃO será instalado.'
Push-Location $root
try {
    New-Item -ItemType Directory -Force .tools/downloads,.tools/bonjour-sdk/Include,.tools/bonjour-sdk/Lib/x64,.cache/research | Out-Null
    $msi = Join-Path $root '.tools\downloads\Bonjour64.msi'
    if (-not (Test-Path -LiteralPath $msi)) {
        Invoke-WebRequest 'https://swcdn.apple.com/content/downloads/52/06/071-03198/djcqm50b49h4o03eetqwowrdpf4o9sx71z/Bonjour64.msi' -OutFile $msi
    }
    if ((Get-FileHash -LiteralPath $msi).Hash -ne '46E31E284DA64D6C2D366352B8A8ABCF7DB28D3E2A870D8FCF15C4A6FE0A6DD1') { throw 'Bonjour MSI hash mismatch.' }
    if ((Get-AuthenticodeSignature -LiteralPath $msi).Status -ne 'Valid') { throw 'Bonjour MSI signature is not valid.' }
    & "$PSScriptRoot\extract-bonjour.ps1" -Installer $msi
    $revision = 'd4658af3f5f291311c6aee4210aa6d39bda82bbe'
    Invoke-WebRequest "https://raw.githubusercontent.com/apple-oss-distributions/mDNSResponder/$revision/mDNSShared/dns_sd.h" -OutFile .tools/bonjour-sdk/Include/dns_sd.h
    if ((Get-FileHash .tools/bonjour-sdk/Include/dns_sd.h).Hash -ne '5D0CA50F207F6EB02E09D743F9B65D2ADE65E8F81862DCD3703845B4FE87A9C1') { throw 'DNS-SD header hash mismatch.' }
    if (-not (Test-Path .cache/research/UxPlay-1.73.7/uxplay.cpp)) {
        & git clone --depth 1 --branch v1.73.7 https://github.com/FDH2/UxPlay.git .cache/research/UxPlay-1.73.7
        if ($LASTEXITCODE -ne 0) { throw 'Could not obtain UxPlay source.' }
    }
    $commit = & git -C .cache/research/UxPlay-1.73.7 rev-parse HEAD
    if ($commit -ne 'df67c212a433cf6dda3676dd40c097900d24e645') { throw 'Unexpected UxPlay source revision.' }
    $file = Join-Path $root '.cache\research\UxPlay-1.73.7\uxplay.cpp'
    $source = [IO.File]::ReadAllText($file)
    if (-not $source.Contains('iMirror: live redirected debug logs')) {
        $pattern = 'if \(!debug_log\) \{\s*setbuf\(stdout, NULL\);\s*\}'
        if (-not [regex]::IsMatch($source,$pattern)) { throw 'Unexpected source for stdout patch.' }
        $source = [regex]::Replace($source,$pattern,'setbuf(stdout, NULL); // iMirror: live redirected debug logs')
        [IO.File]::WriteAllText($file,$source)
    }
    $env:MSYSTEM='UCRT64'; $env:CHERE_INVOKING='1'
    $env:BONJOUR_SDK_HOME = Join-Path $root '.tools\bonjour-sdk'
    & $bash -lc 'cd .tools/msys64/ucrt64/bin && gendef dnssd.dll && dlltool -d dnssd.def -D dnssd.dll -l ../../../bonjour-sdk/Lib/x64/dnssd.lib'
    if ($LASTEXITCODE -ne 0) { throw 'DNS-SD import library build failed.' }
    & $bash -lc 'cmake -S .cache/research/UxPlay-1.73.7 -B .cache/uxplay-build -G Ninja -DCMAKE_BUILD_TYPE=Release -DNO_MARCH_NATIVE=ON -DCMAKE_INSTALL_PREFIX=/ucrt64 && cmake --build .cache/uxplay-build -j2'
    if ($LASTEXITCODE -ne 0) { throw 'UxPlay native build failed.' }
    Copy-Item .cache/uxplay-build/uxplay.exe .tools/msys64/ucrt64/bin/uxplay.exe
    & $bash -lc 'pacman -Q' | Set-Content .tools/msys2-packages.txt
    Write-Host 'Build local pronto. Instale Bonjour manualmente para anunciar o receiver; veja README.'
} finally { Pop-Location }
