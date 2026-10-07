# Dependências externas — fase 2

Nenhum fonte GPL é compilado/linkado nos assemblies C#. UxPlay é processo externo.
Licença upstream e patch desse build neste diretório. Binários/fontes baixadas/pacotes
locais em .tools/.cache não integram o código distribuído.

| Componente | Origem / versão |
| --- | --- |
| UxPlay | https://github.com/FDH2/UxPlay, v1.73.7, df67c212a433cf6dda3676dd40c097900d24e645, GPLv3 |
| Patch | uxplay-1.73.7-live-logs.patch remove condição !debug_log do setbuf Windows; sem alteração de protocolo/renderer. |
| Fonte local | .cache/research/UxPlay-1.73.7; patch separado e UxPlay-LICENSE preservada. |
| Build | GCC16.2.0-4, CMake4.4.4-3, Ninja1.13.2-1, UCRT64, Release, NO_MARCH_NATIVE=ON |
| GStreamer | Pacotes MSYS2 UCRT64 1.28.7-1: gstreamer/base/good/bad/libav; licenças variam por plugin/biblioteca transitiva. |
| Dependências | libplist2.7.0-4, OpenSSL3.6.5-1; lista completa .tools/msys2-packages.txt |
| Bonjour | MSI Apple3.1.0.1; dnssd64.dll extraída e renomeada dnssd.dll local, sem serviço instalado; licenças Apple próprias. |
| Cabeçalho | apple-oss-distributions/mDNSResponder revisão d4658af3f5f291311c6aee4210aa6d39bda82bbe; copyright/licença preservados em .tools/bonjour-sdk/Include/dns_sd.h. |

SDK local usa cabeçalho público Apple e import library gerada das exports x64 oficiais
via gendef/dlltool. Sem SDK de terceiros ou Bonjour SDK global. DLL não substitui serviço.

Hashes SHA256 da preparação em 2026-10-05:

- UxPlay.exe build final: BBEDA5A9757178BC99186F7CA58C80A9ABD47A7EA2DED8043EEAA532B75FD33E.
- Bonjour64.msi: 46E31E284DA64D6C2D366352B8A8ABCF7DB28D3E2A870D8FCF15C4A6FE0A6DD1; Authenticode Valid.
- dns_sd.h: 5D0CA50F207F6EB02E09D743F9B65D2ADE65E8F81862DCD3703845B4FE87A9C1.
- MSYS2 tar.xz: EA2F31A0B6ADE63914CE441FFB022F0F6AA96982BFEFA2326460A26D5FB01322.

Recompilação pode produzir outro hash; registrar novamente ao distribuir. Antes de
redistribuir o conjunto, revisar licenças de todos os pacotes, fontes correspondentes,
patches e avisos aplicáveis. Separação de processo não declara dispensa de obrigações.
O release local agora inclui somente a closure de DLLs e plugins verificados,
manifesto SHA256, estas instruções, a licença UxPlay e o patch. As licenças disponíveis
dos pacotes MSYS2 estão em runtime/airplay/licenses. Não foi publicada uma release binária
no GitHub nesta tarefa; apenas fonte e documentação são enviados por Git.
windows-ble-hid segue referência MIT da fase3; o Report Map adaptado e seu aviso estão
em src/iMirror.Bluetooth/HidSchema.cs.
