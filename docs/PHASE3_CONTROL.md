# Controle BLE HID e UX

- **PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED**
- **PHASE 3 INPUT: PHYSICALLY VALIDATED**
- **PHASE 3 UX: UPDATED — PENDING USER VALIDATION**

O usuário confirmou no iPhone 14/iOS 27.0.1: pairing HOGP, subscribers de mouse/teclado, cursor AssistiveTouch, movimento, clique, letras básicas e controle simultâneo ao AirPlay. Wheel, reconexão, rotação e as novas opções de UX precisam de confirmação específica; não foram promovidos pelo teste local.

## Configurar e testar a UX

1. Inicie AirPlay, conecte o iPhone ao receiver e aguarde vídeo externo. Conecte controle Bluetooth e aguarde os dois subscribers.
2. No iPhone: **Ajustes > Acessibilidade > Toque > AssistiveTouch = ON**. Ative **Mostrar Teclado na Tela** se quiser continuar usando o teclado virtual. Deixe **Controle de Permanência** e **Teclas do Mouse OFF**; não é necessário ativar **Adaptações de Toque**.
3. Selecione **PortugueseBrazilAbnt2**, ou Auto com Português Brasil ativo no Windows. No iPhone: **Ajustes > Geral > Teclado > Teclado Físico**, escolha o layout correspondente. Para US, selecione UnitedStates nos dois lados.
4. Ajuste **Velocidade do cursor** entre 0,25x e 3x; 1x é o padrão. Alterar a opção encerra a captura: reative depois de ajustar.
5. Clique **Ativar controle** e entre no viewport. Deve aparecer somente o cursor AssistiveTouch. Sair do viewport, ESC, Ctrl+Alt+Q, perda de foreground, desconexão ou parada deve restaurar o cursor local. Teste também parar enquanto tecla/botão estiver pressionado.
6. Em um campo vazio, teste `aspas simples e duplas`, `? / \ | : ; , . < > [ ] { } - _ = + @`, acentos isolados e `á à â ã é ê í ó ô õ ú ç`. Teclas mortas seguidas de Space geram o acento isolado.
7. Reporte cursor oculto/restaurado, velocidades 0,25/1/3x, símbolos, acentos, wheel, portrait/landscape e reconexão. Pare com ESC antes de mudar de janela ou encerrar.

## Implementação e segurança

- HOGP: HID 0x1812, Battery 0x180F, criptografia e CCCD gerenciados pelo Windows. Notify direcionado ao subscriber do host selecionado. Pairing e características existentes preservados.
- Report Map: teclado ID1/8 bytes e mouse ID2/6 bytes. A única alteração necessária ao layout é o máximo de usages do teclado: 0x65 → **0x87**, incluindo International1 `/ ?` do ABNT2. Máximo lógico usa representação positiva de 16 bits. IDs, tamanhos, modificadores e descriptor do mouse são iguais ao upstream validado. O teste compara todo o restante byte a byte com a fixture original.
- Se o iOS mantiver o descriptor antigo em cache, somente as teclas extras podem exigir esquecer/reparear o PC. Isso é uma possibilidade, não uma etapa automática de instalação.
- Captura: hooks temporários em MTA, worker separado, HWND Gst pertencente ao receiver atual, foreground, viewport/aspect ratio e DPI físico. Outros HWNDs sobre o vídeo também ficam fora da captura. Fila de 128, coalescing apenas entre movimentos consecutivos e pacing mínimo de 16 ms após cada envio.
- Saída do viewport descarta a fila e registra release pendente com geração da fila. Uma saída/reentrada rápida não perde o release. Teclas previamente pressionadas ficam locais até key-up.
- Parada desativa o envio e devolve input/cursor imediatamente; o pump mantém hooks em modo pass-through enquanto o worker drena o envio em andamento e tenta keyboard neutral → mouse neutral. Depois remove hooks, encerra a superfície e zera acumuladores. **Stopped** só é registrado após a conclusão. Sem link, o transporte mantém sincronização neutra pendente.
- Cursor: testes reais retornaram Access Denied ao alterar a classe do processo GStreamer. A solução usa superfície Win32 própria, sem ativação, acima somente do viewport. WM_SETCURSOR executa no nosso thread; não usa ShowCursor, SetSystemCursor, AttachThreadInput nem subclassing externo. A superfície é layered com alpha **1/255**, necessário para hit testing (alpha zero deixa o mouse passar), e desaparece em cleanup/process exit. Não há confinamento do ponteiro nem reparenting do vídeo.
- Velocidade: multiplicação linear dos deltas antes do clamp HID, com resto fracionário e descarte de overflow. Sem aceleração própria.
- Teclado: scan codes OEM e usages padrão; Auto lê o HKL do thread do receiver. Letras/dígitos/modificadores seguem estados físicos. Dead keys e releases são encaminhados; o layout de hardware do iOS compõe os caracteres. A API diagnóstica de composição produz press + neutral para cada tecla, sem Unicode falso.
- Consumer Control/Eject não foi adicionado: não foi encontrada comprovação suficiente de Show Keyboard confiável no iOS. A orientação usa a [opção oficial da Apple](https://support.apple.com/guide/iphone/use-assistivetouch-iph96b21954/ios).

## Evidências locais

Debug/Release e suites das Fases 1–3 verificam lifecycle, input, fila, pacing, modifiers, todos os símbolos/acentos e ausência de novos envios após parada. O probe isolado conserva seus próprios testes de reports/releases.

O teste `--native-cursor` usa videotestsrc → d3d11videosink e **transporte HID falso**. Verifica GetCursorInfo dentro/fora do viewport, reentrada e após Stop, foreground preservado, hooks reais, desconexão simulada e reativação. Não é teste AirPlay nem prova de ação no iPhone.

Logs locais, ignorados pelo Git: `logs/phase3b-debug-validation.log`, `logs/phase3b-release-validation.log`, `logs/phase3b-native-console.log`, `logs/bluetooth-control.log`. Logs normais não contêm texto digitado nem cada movimento. Notify Success comprova transporte apenas.

O Windows deste PC rejeita Appearance adicional com 0x80070005; o HOGP normal continua padrão. GattServiceProvider não possui Dispose no SDK usado: cleanup para advertising, remove handlers/monitores e solta referências; o serviço nativo termina com o processo. Nunca há liberação COM forçada. [API Microsoft](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovider).

Referências: [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid), [Microsoft VirtualKeyboard](https://github.com/microsoft/BluetoothLEExplorer/blob/master/BluetoothLEExplorer/BluetoothLEExplorer/Models/VirtualKeyboard.cs), [layout ABNT2 da Microsoft](https://github.com/MicrosoftDocs/globalization/blob/main/globalization/keyboards/kbdbr_2.html), [layered windows e hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows). Avisos MIT preservados.

**AirPlay, binário UxPlay, perfis, d3d11videosink, Bonjour, firewall e rede não foram alterados nesta fase.**
