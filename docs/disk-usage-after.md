# Uso de disco após redução e release

Snapshot: 2026-10-06T22:59:52.1363663-03:00. Somatório dos tamanhos lógicos dos arquivos somente deste projeto, sem seguir reparse points. MiB/GiB usam base 1024. Pastas cumulativas não devem ser somadas.

| Medição | MiB | Bytes |
|---|---:|---:|
| Projeto antes | 4267,70 | 4475012160 |
| Projeto depois, incluindo dist e rollback | 1692,56 | 1774774729 |
| Espaço liberado líquido | 2575,15 | 2700237431 |
| Release principal framework-dependent | 200,22 | 209942856 |
| ZIP portátil self-contained | 133,38 | 139854535 |
| EXE principal | 24,71 | 25907936 |
| Portátil extraído | 333,29 | 349474825 |
| Runtime AirPlay com licenças/manifesto | 175,51 | 184030660 |
| SDK + runtimes .NET locais (desenvolvimento) | 769,94 | 807336133 |
| Rollback nativo compactado | 173,91 | 182360577 |
| dist inteira (principal + ZIP) | 333,59 | 349797391 |

Antes: **4,168 GiB**. Depois: **1,653 GiB**. Redução líquida: **60,3%**, mesmo acrescentando EXE, ZIP e rollback.

## Maiores itens restantes

| Item | Tipo | MiB |
|---|---|---:|
| .tools | folder | 1130,12 |
| .tools\dotnet | folder | 769,94 |
| .tools\dotnet\sdk | folder | 399,01 |
| .tools\dotnet\sdk\10.0.401 | folder | 399,01 |
| dist | folder | 333,59 |
| dist\iMirror | folder | 200,22 |
| .tools\dotnet\shared | folder | 199,15 |
| .tools\msys64 | folder | 177,25 |
| .tools\msys64\ucrt64 | folder | 176,58 |
| dist\iMirror\runtime\airplay | folder | 175,51 |
| dist\iMirror\runtime | folder | 175,51 |
| .tools\rollback | folder | 173,91 |
| .tools\rollback\airplay-original-runtime.zip | file | 173,91 |
| .tools\msys64\ucrt64\bin | folder | 170,23 |
| dist\iMirror\runtime\airplay\bin | folder | 169,37 |
| .tools\dotnet\packs | folder | 160,84 |
| dist\iMirror-Portable.zip | file | 133,38 |
| .cache | folder | 110,02 |
| .tools\dotnet\shared\Microsoft.WindowsDesktop.App | folder | 94,37 |
| .tools\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.12 | folder | 94,37 |
| .tools\dotnet\sdk\10.0.401\DotnetTools | folder | 83,36 |
| .cache\nuget | folder | 79,70 |
| .tools\dotnet\shared\Microsoft.NETCore.App\10.0.12 | folder | 76,30 |
| .tools\dotnet\shared\Microsoft.NETCore.App | folder | 76,30 |
| .cache\nuget\microsoft.windows.sdk.net.ref\10.0.19041.57 | folder | 74,61 |
| .cache\nuget\microsoft.windows.sdk.net.ref | folder | 74,61 |
| .tools\dotnet\sdk\10.0.401\Sdks | folder | 71,17 |
| .tools\dotnet\sdk\10.0.401\FSharp | folder | 62,47 |
| .tools\dotnet\packs\Microsoft.WindowsDesktop.App.Ref | folder | 58,91 |
| .tools\dotnet\packs\Microsoft.WindowsDesktop.App.Ref\10.0.12 | folder | 58,91 |

## Composição e decisões

Maior arquivo: .tools\rollback\airplay-original-runtime.zip, 173,91 MiB. Maior pasta: .tools, 1130,12 MiB; seu principal componente é o SDK .NET, necessário para continuar desenvolvendo.
UxPlay 1.73.7: 0,64 MiB. Plugins GStreamer selecionados: 3,51 MiB. DLLs do bin nativo: 168,48 MiB. GStreamer 1.28.7 e a closure PE compartilham dependências; não somar estes valores ao runtime total.
O release contém 142 arquivos nativos selecionados, além de licenças, manifesto e receita/patch do UxPlay. O runtime local mantém dois plugins de encoder exclusivamente para os testes. Assets visuais são vetores/XAML embutidos no EXE; não existe pasta de assets externa nem download de fontes/imagens.
A versão principal exige .NET Desktop Runtime 10 x64 (ou usa o .NET local neste checkout). O ZIP inclui .NET/WPF: diferença de 133,07 MiB quando extraído. Não inclui SDK. Single-file sem trimming preserva WPF e WinRT.

Removidos: cache pacman, headers, bibliotecas estáticas, gcc/Python/MSYS de desenvolvimento, downloads de preparo, publicações antigas/intermediárias, cópia extraída usada no teste do ZIP, builds Debug já testados e caches locais dos runtime packs usados na publicação. Próximo build-release poderá baixar novamente esses pacotes Microsoft para o cache local; nenhum cache global foi limpo.
Preservados: código, .git, testes, documentação, configurações, evidência física, SDK e referências WinRT, runtime de recepção, testes Release, fonte GPL correspondente e rollback SHA256 de 821 arquivos. O rollback permite restaurar DLLs/plugins no mesmo caminho com tools/restore-native-runtime.ps1; toolchain para recompilar UxPlay deverá ser preparado novamente.

A medição não atribui mudanças de espaço livre total do disco à tarefa. Git e documentação adicionados depois do snapshot podem alterar alguns KiB. Nenhuma pasta externa, WSL, Docker, firewall ou componente instalado do Windows foi modificado.

PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED
PHASE 3 INPUT: PHYSICALLY VALIDATED
PHASE 3 UX: UPDATED — PENDING USER VALIDATION
