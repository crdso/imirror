# Pesquisa técnica — fase 0

Data: 05/10/2026. Escopo desta entrega: pesquisa e base compilável (fases 0 e 1).
Nenhum receptor, anúncio Bluetooth ou captura de input foi implementado.

## Método e evidências

Foram lidos README, licença e trechos de código dos dois projetos principais.
Os clones de consulta ficam em `.cache/research`, ignorados pelo projeto e sem
referência nos arquivos de compilação. Não se executou nenhum desses projetos.

| Referência | Revisão consultada | Arquivos analisados |
| --- | --- | --- |
| [FDH2/UxPlay](https://github.com/FDH2/UxPlay) | `3dbf7ceee65932154e85a2f83963d53520a799fa` | README.md, LICENSE, CMakeLists.txt, lib/CMakeLists.txt, renderers/video_renderer.c, uxplay.cpp |
| [abhishek-raj/windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid) | `7d66ef199cc9d3065c7bf0d1bf8379462674e71a` | README.md, DEVELOPMENT.md, LICENSE, BleHid.Core.csproj, BleHidPeripheral.cs, HidDescriptors.cs, HidReports.cs, AppearanceAdvertiser.cs |
| [gosom/airplay](https://github.com/gosom/airplay) | indisponível | A API pública `https://api.github.com/repos/gosom/airplay` respondeu HTTP 404; não se presume conteúdo, licença ou suporte. Pode ter sido removido, renomeado ou tornado privado. |
| [FD-/RPiPlay](https://github.com/FD-/RPiPlay) | README público consultado em 05/10/2026 | Comparação histórica, dependências e licença; não clonado nem integrado. |

## UxPlay

O [README](https://github.com/FDH2/UxPlay/blob/3dbf7ceee65932154e85a2f83963d53520a799fa/README.md)
documenta execução em Windows 10/11, compilação MSYS2/MinGW (UCRT64),
GStreamer, OpenSSL e libplist. O master se identifica como 1.74 experimental:
introduz mDNS interno; builds anteriores usam Bonjour. Não confundir master
experimental com release estável validada. O receptor usa protocolos AirPlay
obtidos por engenharia reversa, incluindo modo legado; não é uma API Apple
oficial de receptor Windows. Conteúdo protegido por DRM não é suportado.
As opções `-n`, `-vsync no` e `-vrtp` permitem nome, menor sincronização e saída
RTP, respectivamente. Suporte declarado pelo upstream não prova funcionamento
neste notebook ou na versão de iOS do usuário.

A leitura de [lib/CMakeLists.txt](https://github.com/FDH2/UxPlay/blob/3dbf7ceee65932154e85a2f83963d53520a799fa/lib/CMakeLists.txt)
mostra biblioteca C de protocolo, pthread, playfair, llhttp, DNS-SD/mDNS,
libplist e OpenSSL, com Winsock no Windows. O executável C++ organiza a sessão
e callbacks; vídeo chega ao renderer via `appsrc` e segue um pipeline GStreamer.
Isso separa protocolo de decodificação/apresentação, mas não fornece uma API C#.

Em [video_renderer.c](https://github.com/FDH2/UxPlay/blob/3dbf7ceee65932154e85a2f83963d53520a799fa/renderers/video_renderer.c),
`video_renderer_set_window_handle` e `gst_video_overlay_set_window_handle`
existem. Não foi encontrado argumento CLI que exponha esse handle em
`uxplay.cpp`; portanto **não pressupor que o executável original aceita um HWND
do WPF**. `appsink` dentro de outro processo tampouco entrega frames C# sem IPC.

Reutilização recomendada: executável externo, original e versionado, com sua
janela própria primeiro. Implementar em C# descoberta da instalação, configuração,
supervisão, stdout/stderr, encerramento e estado; não reimplementar criptografia
AirPlay nesta fase.

## windows-ble-hid

O [README](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/README.md)
descreve um protótipo funcional C# em user space, com Windows 10 build 19041+,
Bluetooth LE peripheral e .NET 8. Relata pareamento/input em iPhone com
AssistiveTouch; reconexão de iPhone após reinício do app está **não testada**.
Esses relatos não identificam uma matriz completa de modelos/versões de iOS.

A leitura de [BleHidPeripheral.cs](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/src/BleHid.Core/BleHidPeripheral.cs)
mostra `BluetoothAdapter.GetDefaultAsync`, `IsPeripheralRoleSupported`,
`GattServiceProvider.CreateAsync`, características locais, leitura/escrita,
`SubscribedClientsChanged` e `NotifyValueAsync`. Serviço HID `0x1812`, Battery
`0x180F`, Report Map, HID Information, Control Point, Protocol Mode e Report
Reference compõem HOGP. Os relatórios têm proteção de criptografia. O anúncio
conectável vem de `GattServiceProvider.StartAdvertising`.

[HidDescriptors.cs](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/src/BleHid.Core/HidDescriptors.cs)
define teclado de seis teclas simultâneas e modificadores (ID 1), e mouse de
três botões com X/Y relativos de 16 bits e wheel (ID 2).
[HidReports.cs](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/src/BleHid.Core/HidReports.cs)
separa codificação; o ID de relatório fica no descriptor Report Reference,
sem prefixo no payload HOGP. Conversão de texto é baseada em ASCII/layout US;
não cobre automaticamente português, acentos e Unicode.

[DEVELOPMENT.md](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/DEVELOPMENT.md)
relata ciclo de vida GATT vinculado ao processo, problemas de cache/reconexão
em outros hosts, coalescência de movimento e preservação de transições de teclas.
Lista de subscribers pode sobreviver à perda física do link; não basta contar
subscribers para afirmar conexão. O input upstream usa hooks globais; iMirror
precisará adaptar esse conceito à captura restrita à janela, com ESC e liberação
na perda de foco/conexão. Não reutilizar o bloqueio global por padrão.

`BluetoothLEAdvertisementPublisher` não substitui servidor GATT/HOGP.
[AppearanceAdvertiser.cs](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/src/BleHid.Core/AppearanceAdvertiser.cs)
é uma tentativa diagnóstica de anúncio paralelo; não assumir que altera identidade
GAP, nome do computador ou vinculação HID do sistema.

Reutilização possível após auditoria MIT: ideias de layout HOGP, codificação,
diagnósticos e pacing. A implementação efetiva de `BluetoothHidService` começa
apenas na fase 3, depois de validar vídeo. Nenhum código foi copiado agora.

## Referência alternativa

[RPiPlay](https://github.com/FD-/RPiPlay) tem licença GPLv3 e usa C/C++,
OpenSSL/libplist/Avahi e renderers Broadcom/OpenMAX ou GStreamer. É útil para
entender a linhagem do receptor e callbacks, porém sua documentação foca Pi/Linux;
não oferece vantagem sobre UxPlay para este aplicativo Windows. Não será integrado.

## APIs e limites das plataformas

A [Microsoft documenta GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)
com serviços locais, notificações e anúncios conectáveis. GAP, GATT, DIS e Scan
Parameters são reservados; criação pode falhar com `DisabledByPolicy`. CCCD é
gerado automaticamente para características notificáveis. Apps empacotados devem
declarar capability `bluetooth`; a fase 3 deverá verificar também o comportamento
do desktop sem pacote no Windows alvo. Não se decide empacotamento por suposição.

[WinRT em desktop](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
é acessível a C# moderno por TFM Windows versionado. Assim WPF não impede uso de
BLE. Chamadas exclusivas de Windows 11 precisarão de guardas de versão. A capacidade
de periférico deve ser consultada, independentemente do número comercial Bluetooth.
Se o rádio integrado não suportar peripheral, o requisito sem hardware adicional
fica inviável neste notebook; registrar o bloqueio, sem trocar silenciosamente por USB.

A [Apple suporta mouse Bluetooth com AssistiveTouch](https://support.apple.com/en-gb/111775).
Configuração: Ajustes → Acessibilidade → Toque → AssistiveTouch → Dispositivos →
Dispositivos Bluetooth. Ativar AssistiveTouch para exibir o cursor. HID controla
um ponteiro de acessibilidade, não injeta toques arbitrários nem promete equivalência
a todos os gestos multitouch.

## Integração visual: hipóteses para a fase 2

| Opção | Avaliação e teste necessário |
| --- | --- |
| Janela própria do UxPlay | Primeira prova de vídeo, menor trabalho; aceita pelo pedido. Não chamar de vídeo embutido. |
| HWND via `GstVideoOverlay` | [API GStreamer](https://gstreamer.freedesktop.org/documentation/video/gstvideooverlay.html) fornece renderização em janela nativa; avaliar sink Direct3D + HwndHost. O código upstream não expõe HWND pela CLI; adaptação seria um executável GPL separado e auditado. |
| RTP local + renderer do app | `-vrtp` entrega fluxo comprimido para um receptor local independente. Investigar GStreamer no processo do app com sink nativo; limitar bind ao loopback, filas e jitter. Exige pipeline/versionamento e medição de latência. |
| Frames via `appsink` | [API appsink](https://gstreamer.freedesktop.org/documentation/app/appsink.html) oferece samples; em processo externo precisa IPC. Cópias BGRA/WriteableBitmap e filas podem aumentar latência; só prototipar após vídeo básico aprovado. |
| Captura/reparent de janela | Riscos de DPI, resize, foco e custo de cópias. Não é a estratégia principal; nenhuma coordenada fixa ou OCR. |

As avaliações da tabela são inferências de engenharia, ainda sem benchmark.
Não há promessa de 60 FPS ou latência fixa.

## Licenças e distribuição

[UxPlay LICENSE](https://github.com/FDH2/UxPlay/blob/3dbf7ceee65932154e85a2f83963d53520a799fa/LICENSE)
é GPLv3; arquivos consultados permitem GPLv3 ou posterior. Não adicionar arquivos,
linking estático/dinâmico ou P/Invoke dessa implementação aos assemblies próprios.
Processo externo é a fronteira inicial proposta, **não garantia automática de
isenção GPL**. Antes de distribuir: avaliar natureza da comunicação e do conjunto,
licenças transitivas, avisos e fornecimento do código-fonte correspondente de
binários/modificações GPL. Não há redistribuição nesta entrega.

[windows-ble-hid LICENSE](https://github.com/abhishek-raj/windows-ble-hid/blob/7d66ef199cc9d3065c7bf0d1bf8379462674e71a/LICENSE)
é MIT: reutilização futura precisa preservar copyright e licença. Dependências
GStreamer/plugins/codecs têm obrigações próprias; gerar inventário da versão
efetivamente distribuída. Licença final do código original iMirror fica a definir
pelo proprietário; não escolher uma licença comercial/open source por ele.

## Riscos e decisões

| Risco | Impacto | Tratamento previsto |
| --- | --- | --- |
| iOS mudar protocolos do receptor | Quebra de descoberta/handshake | Fixar versão UxPlay validada e registrar modelo/iOS nos testes. |
| Multicast filtrado, firewall, VPN, AP isolation | Receptor invisível | Diagnóstico de interface/rede e regras restritas aprovadas na fase 2; não desligar firewall. |
| Rádio sem LE peripheral | Controle HID inviável sem hardware extra | Probe real na fase 3 e erro explícito; requisito preservado. |
| Pareamento, criptografia, subscriptions e cache | Pareado sem receber input | Validar leitura Report Map, subscriptions, link e efeito no iPhone separadamente. |
| Mouse relativo sem feedback de posição | Clique absoluto impreciso | Mapper geométrico não resolve aceleração/posição inicial; fase 5 exige prova de calibração/estratégia suportada. Sem OCR, sem coordenadas fixas, sem prometer exatidão. |
| Layout de teclado e IME | Texto incorreto | Mapear layout do iPhone e tratar caracteres não suportados; HID não transporta texto Unicode diretamente. |
| Foco ou link perdidos com botão/tecla pressionados | Input preso | Liberação de estados, ESC local e fila ordenada nas fases de input. |
| Decodificação e transferência de frames | Latência/uso de CPU | Medir antes de escolher integração; filas limitadas e descarte de frames atrasados. |

Estratégia: validar esta base; depois UxPlay externo e vídeo real; somente após
aprovação dos testes de vídeo iniciar BLE. Cada fase tem um registro de aceitação.
Não executar fases 2–11 nesta entrega.
