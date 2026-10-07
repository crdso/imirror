# Polimento final — validação e teste físico

Data: 2026-10-07. Base: main / 8635a8a, working tree inicialmente clean; pull --ff-only confirmou sincronização. Logs brutos, screenshots e pacotes ficam fora do Git.

## Diagnóstico e lifecycle HID

O código anterior destruía HOGP tanto pelo botão Bluetooth quanto ao parar AirPlay. O log físico continha várias sequências CreateAsync/advertising/cleanup sem subscribers. Esse churn foi confirmado; sua contribuição para cache/bond do iOS é uma hipótese que precisa do novo teste físico. Conexão à entrada Classic também é uma possibilidade quando não há sessão/read HID. Não há evidência para declarar bonds corrompidos ou eliminar duas entradas no iPhone.

Agora Connect é idempotente, inclusive concorrente. Provider vive até shutdown ou reset avançado confirmado; timeout de 30s, Stop AirPlay, Stop Control e reconexão não o recriam. Advertising é retomado no mesmo provider somente quando necessário, com uma solicitação por evento/recuperação e sem loop. BAS recebe StopAdvertising antes do HID durante cleanup. Reset libera captura, envia neutral quando possível, remove handlers, libera referências e recria uma vez somente após parada confirmada. Se HID/BAS ainda reportar Started ou a parada falhar, o controller mantém o bloqueio de instância e recusa outro provider na mesma sessão; a UI orienta fechar/reabrir o app. GattServiceProvider não expõe Dispose/IClosable no SDK usado; cleanup não apaga bonds/caches do iPhone nem promete reconstruir o estado interno do Windows.

Bond, sessão GATT, metadata, CCCD e efeito físico são separados. A UI mostra as etapas do host selecionado; não combina subscriptions de hosts diferentes nem muda silenciosamente de host após desconexão. Controle pronto exige Mouse e Keyboard reais; mouse-only/keyboard-only são identificados. Capture nunca recomeça automaticamente. Logs correlacionam session/id aleatório local e geração, sem PIN, MAC ou payload de input.

O anúncio paralelo Appearance foi desativado na aplicação, preservando o HOGP validado. O [GattServiceProviderAdvertisingParameters oficial](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovideradvertisingparameters?view=winrt-19041) não oferece LocalName neste contrato; nenhum segundo publisher foi criado para tentar renomear o receiver BLE. A referência [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid/blob/main/DEVELOPMENT.md#provider-lifetime-is-service-lifetime) sustenta manter o provider residente; seus workarounds Android não foram aplicados ao iOS.

## Janelas, ícones e modo foco

As duas imagens fornecidas foram preservadas em Assets/Branding, com ICO real de 16/20/24/32/40/48/64/128/256 px. Resources e ApplicationIcon são relativos e embutidos. O ícone azul pertence ao EXE/painel/tray; o vermelho é aplicado via WM_SETICON ao HWND Gst encontrado pelo PID pertencente ao receiver. HICON permanece vivo até a janela terminar ou os ícones antigos serem restaurados; o título é iMirror — iPhone. UxPlay não foi alterado.

WorkArea e DPI reais determinam o tamanho inicial, limitado a 85%; último bounds normal é validado e centralizado/clamped quando necessário. Fullscreen/maximized não são gravados como tamanho normal. Renderer usa CLIENT RECT com decoração calculada por AdjustWindowRectExForDpi, aspect do stream e debounce de 250 ms. Resize manual é respeitado até mudar HWND/resolução/opção.

Modo foco só oculta com Streaming + HWND visível + bandeja/atalho disponíveis. Ctrl+Alt+I recupera o painel; conflito usa Ctrl+Alt+Shift+I; se ambos falharem, ocultação fica bloqueada. Durante captura, somente o atalho registrado é interceptado para liberar input e mostrar o painel; ESC/Ctrl+Alt+Q permanecem intactos. Tray oferece Mostrar iMirror, Parar controle e Sair. Perda do renderer/stream/receiver ou erro mostra o painel. Fullscreen mantém estado separado.

Verbose tem histórico limitado separado, preservando eventos relevantes na UI; arquivo completo continua rotativo.

## Evidência automática

| Verificação | Resultado |
|---|---|
| Debug e Release | 0 warnings / 0 errors |
| Suites por configuração | UI 10/10, AirPlay 10/10, input/BLE 38/38; antigos e novos preservados |
| Geometria | 1366×728 e 1920×1040 WorkArea, monitores negativos, área pequena, bounds inválidos/oversized, portrait/landscape e DPI 96/144/192 |
| Viewports | Cinco páginas em 820×520, 960×600, 1366×768, 1920×1080, 2560×1440; scroll e diagnóstico limitado |
| Renderer real local | Ícones SMALL/BIG vermelhos verificados por pixels, título, client aspect, debounce, resize manual e auto on/off |
| Recuperação real local | Tray/hotkey registrados; WM_HOTKEY mostra painel; Stop Stream/fechar renderer recuperam; painel oculto fecha normalmente |
| HOGP boundary tests | Connect concorrente único, timeout, reconexão, reset único, shutdown idempotente e evidência por camada |
| Provider real no PC | Advertising Started; Connect repetido manteve geração 1; timeout visual após 31s, provider mantido; nenhum key/click/movement enviado |
| Reset nativo | PASS: parada não confirmada bloqueou reset e novo Connect, geração permaneceu 1; nenhum segundo provider criado |
| Cleanup nativo | StopAdvertising solicitado para BAS e HID; após 2s Windows ainda reportou Started. Handlers/referências liberados e processo encerrado; não se afirma remoção de registro/bond durante reset interno |
| Pacote | PASS: EXE direto e ZIP self-contained extraído em pasta independente, PATH sem SDK/DOTNET_ROOT, ícones Shell/SMALL/BIG/atalho azuis, bounds 956×582 dentro da WorkArea; 142 hashes nativos por pacote conferidos. Ver FINAL_POLISH_RELEASE.md |
| Cursor nativo | PASS: invisível dentro do viewport, visível fora/depois de Stop, reentrada e foreground preservados; transporte HID fake, sem input no iPhone |

Não houve nova prova física de pairing/reconnection/click/wheel/keyboard no iPhone. A reconstrução de handles e o efeito físico do reset precisam dessa validação, especialmente devido ao status Started residual observado no cleanup. O teste do renderer usa videotestsrc local, sem AirPlay real. O smoke test publicado exige a sessão interativa: execução isolada pelo sandbox não expõe MainWindowHandle; a execução na sessão do usuário passou. A extração foi feita dentro do workspace autorizado, fora da árvore fonte/publish original, sem depender de seus assets.

## Teste ao chegar ao iPhone

1. Abra iMirror e Iniciar AirPlay; no iPhone, selecione iMirror - Windows. Confira janela iMirror — iPhone, ícone vermelho e tamanhos portrait/landscape. Teste auto-size desligado e resize manual.
2. Controle → Conectar Bluetooth. Pareie em Ajustes → Bluetooth e acompanhe HID Information/Report Map/Keyboard/Mouse. Com duas entradas, teste uma; sem atividade HID, esqueça apenas aquela e tente a outra. AssistiveTouch serve para exibir o ponteiro.
3. Ative controle; teste movimento/click/wheel/teclado. Pare AirPlay e reconecte: provider deve manter geração, subscribers podem voltar e captura exige novo clique.
4. Configurações → modo foco. Volte pela bandeja e pelo atalho; feche o renderer e confirme retorno automático. Use reset HID apenas se precisar, após ler a confirmação.

Reporte a etapa onde parou, subscriptions, funcionamento do input, reconexão e modo foco. Logs locais: logs/bluetooth-control.log no checkout; fora dele, %LOCALAPPDATA%/iMirror/logs.

PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED

PHASE 3 INPUT: PHYSICALLY VALIDATED

PHASE 3 UX: UPDATED — PENDING USER VALIDATION

READY FOR FINAL PHYSICAL VALIDATION
