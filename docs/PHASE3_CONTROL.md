# Controle BLE HID no iMirror

**PHASE 3: IMPLEMENTED — PENDING PHYSICAL VALIDATION**

## Usar

1. No iMirror, **Iniciar AirPlay**; no iPhone, Espelhamento de Tela → **iMirror - Windows**.
2. **Conectar controle Bluetooth**. No iPhone, **Ajustes > Bluetooth**, toque no nome deste PC e confirme o pareamento quando solicitado.
3. Para visualizar o cursor, ative **Ajustes > Acessibilidade > Toque > AssistiveTouch**. Teclado não exige AssistiveTouch.
4. Aguarde **Mouse: conectado** e, para digitar, **Keyboard: conectado**. Bond/advertising sozinhos não habilitam controle.
5. **Ativar controle**, mova o mouse para o vídeo. O click atua no cursor relativo do iPhone, sem promessa de toque absoluto no ponto do mouse do Windows.
6. **ESC**, **Ctrl+Alt+Q**, perda de foco da janela de vídeo ou **Desativar controle** devolvem input ao Windows. Ative novamente quando quiser controlar.

Confirme movimento, click, wheel positivo/negativo, digitação em campo vazio, release após parar, portrait/landscape, perda de conexão/reconexão e AirPlay simultâneo. Tudo isso continua pendente fisicamente. Após reiniciar HOGP/app ou retomar Windows, reconecte em **Ajustes > Bluetooth** se os subscribers não retornarem. Captura nunca reativa sozinha.

## Arquitetura

- Bluetooth: HID 0x1812 + Battery 0x180F, criptografia, CCCD do Windows, host selecionado e notify direcionado. Report Map idêntico ao upstream: teclado ID1/8 bytes; mouse ID2/6 bytes com X/Y relativos de 16 bits. IDs estão apenas no descriptor. Boot reports separados; boot mouse não tem wheel.
- Input: hooks temporários em thread MTA e worker independente. Somente HWND Gst do receiver pertencente a este iMirror, foreground e viewport válido. Movimento agrupado, fila de 128, intervalo mínimo 16 ms depois de cada envio, releases em erro/stop/perda de foco/link. Dimensões atuais, aspect ratio e DPI físico; sem dupla rotação. ESC é local. Layout físico US; PT-BR/Unicode não garantidos.
- WPF: conectar/desconectar, subscriptions separadas, host, capturar teclado, wheel 1–5 e ativação explícita. O PID é rastreado por uma decoração da fábrica existente, sem mudar argumentos/environment/flags AirPlay.
- Controle direto absoluto/digitizer permanece desabilitado: não há prova física de aceitação pelo iPhone 14/iOS 27.0.1. Geometria não transforma mouse relativo em toque absoluto.

## Limitações verificadas

O anúncio paralelo upstream com UUID 0x1812 e Appearance 0x03C1/0x03C2 foi implementado e testado. Neste Windows 19045, Start retorna **0x80070005**: os tipos AD 0x03 e 0x19 são reservados. A UI informa a limitação e mantém HOGP normal; não faz retry administrativo. [Microsoft Publisher](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher).

GattServiceProvider não expõe Dispose/IClosable no SDK 19041. Cleanup usa StopAdvertising, remove handlers, cancela monitor/timers e solta referências; BluetoothLEDevice é disposed. O registro nativo termina com o processo; não há liberação COM forçada de projeções CsWinRT. Release em link fechado é pendente e sincronizado antes de novo input, sem afirmar entrega após disconnect/kill. Dois ciclos locais de provider foram exercitados no mesmo processo. [Microsoft GattServiceProvider](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovider).

A automação em background pode não receber permissão de foreground do Windows. Nesse caso a ativação falha com segurança antes de instalar hooks. A prova de ativar/parar os hooks pelo clique real no iMirror integra a validação física pendente.

Referências: [windows-ble-hid 7d66ef1](https://github.com/abhishek-raj/windows-ble-hid/tree/7d66ef199cc9d3065c7bf0d1bf8379462674e71a), [Microsoft VirtualKeyboard](https://github.com/microsoft/BluetoothLEExplorer/blob/master/BluetoothLEExplorer/BluetoothLEExplorer/Models/VirtualKeyboard.cs). A Microsoft utiliza formato de teclado diferente; aqui prevalece o descriptor upstream, comparado byte a byte. Licença MIT preservada.

## Evidências e diagnóstico

**logs/bluetooth-control.log**: capacidade, rádio, GATT, advertising, metadata reads, Protocol Mode, subscribers, alias do host, capture, notify, cleanup e exceções sanitizadas. Sem texto digitado, reports, IDs/MAC completos. Notify Success é transporte, não ação física.

Na UI, expanda **Host, diagnóstico HID e opções**: HID Information, Report Map, Protocol Mode escrito, bond/link e subscriptions. SubscribedClientsChanged evidencia CCCD ativo; a API não fornece evento separado de cada escrita CCCD. Zero hosts significa nenhuma sessão/request observada, sem inferir que o iPhone foi rejeitado em uma etapa específica.

Logs de builds/testes: **logs/phase3-debug-validation.log**, **logs/phase3-release-validation.log**, **logs/phase3-native-validation.log**. O probe **Testar-BLE-HID.cmd** é apenas diagnóstico, compartilhando o transporte. Feche o probe antes do BLE no iMirror.

## Arquivos alterados/criados

- src/iMirror.Bluetooth: iMirror.Bluetooth.csproj, BluetoothContracts.cs, BluetoothControlLog.cs, BluetoothController.cs, HogpPeripheral.cs, HidSchema.cs, ManualInput.cs, AppearanceAdvertiser.cs, THIRD_PARTY_NOTICES.md.
- src/iMirror.Input: iMirror.Input.csproj, InputModel.cs, InputCapture.cs, InputSender.cs, VideoWindow.cs, README.md.
- src/iMirror.App: iMirror.App.csproj, App.xaml.cs, MainViewModel.cs, MainWindow.xaml, OwnedReceiverProcessFactory.cs, UnavailableBluetoothController.cs (adaptador só para testes antigos).
- experiments/BleHidProbe: BleHidProbe.csproj, GlobalUsings.cs, Program.cs, README.md; removidas cópias HidPeripheral.cs, HidSchema.cs, ManualInput.cs, ProbeLog.cs em favor do core compartilhado.
- tests/iMirror.Phase1.Tests: iMirror.Phase1.Tests.csproj, Program.cs. Novo tests/iMirror.Phase3.Tests: iMirror.Phase3.Tests.csproj, Program.cs, Fixtures/HidDescriptors.Upstream.cs.
- iMirror.sln, NuGet.Config, tools/test.ps1, README.md, docs/PHASE3_CONTROL.md, docs/PHASE3A_PROBE.md, nota histórica docs/PHASE3_BLE_FEASIBILITY.md.

Nenhum arquivo de src/iMirror.AirPlay, binário UxPlay ou configuração AirPlay foi editado. Bonjour/firewall/WSL/rede/renderer preservados.

