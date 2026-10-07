# Fase 3 — investigação de viabilidade BLE HID

> Histórico da investigação inicial. Estado atual: [PHASE3_CONTROL.md](PHASE3_CONTROL.md). As descrições de stub/ausência de integração abaixo referem-se ao estágio anterior.

Investigação: 2026-10-06. Alvo informado: iPhone 14, iOS 27.0.1.

## Decisão

O Windows dispõe das APIs necessárias para implementar mouse e teclado BLE HID
em user space. O adaptador deste PC declara suporte a peripheral. Recomenda-se
uma prova HOGP nativa isolada antes da integração de captura de input.
Ainda não se pode afirmar controle confiável no iPhone específico ou clique
absoluto no ponto do vídeo. Não foi implementado transporte ou captura nesta
investigação; o código do aplicativo permanece intacto.

## Resultados locais reais

| Consulta | Resultado |
| --- | --- |
| Sistema | Windows 10 Pro, 10.0.19045 |
| Rádio | Realtek Bluetooth Adapter |
| Driver | Realtek, 1.1071.2406.2401 |
| `IsLowEnergySupported` | `true` |
| `IsPeripheralRoleSupported` | `true` |
| `IsCentralRoleSupported` | `true` |
| `IsClassicSupported` | `true` |
| Estado do rádio | `Off` |
| `GattServiceProvider.CreateAsync(0x1812)` | `RadioNotAvailable` |
| Processo do probe | Desktop sem pacote, Windows PowerShell 5.1, sem administrador |
| Anúncio / pareamento / input | Não realizados |

As consultas WinRT foram executadas no Windows real. A criação transitória do
serviço foi tentada uma vez, sem `StartAdvertising`; nenhum provider foi criado.
O rádio desligado impede validar criação e publicação. Esse resultado não
equivale a `DisabledByPolicy`, nem comprova que o WPF/.NET 10 conseguirá publicar
quando o rádio estiver ligado. Isso deve ser verificado no próximo probe.

## APIs e protocolo

A Microsoft documenta [GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server).
HID `0x1812` não está na lista de serviços reservados, que inclui GAP, GATT,
Device Information e Scan Parameters. Seu próprio exemplo
[VirtualKeyboard](https://github.com/microsoft/BluetoothLEExplorer/blob/master/BluetoothLEExplorer/BluetoothLEExplorer/Models/VirtualKeyboard.cs)
cria o serviço HID e o anuncia como conectável.

O caminho BLE requer [HID over GATT](https://www.bluetooth.com/specifications/specs/hid-over-gatt-profile-1-0/)
(HOGP): Windows = HID device/peripheral/GATT server; iPhone = HID host/central.
Anunciar um UUID com `BluetoothLEAdvertisementPublisher` sozinho não implementa
HID. Também não basta parear pelo painel Bluetooth clássico do Windows.

APIs efetivas:

- `BluetoothAdapter.GetDefaultAsync` e `IsPeripheralRoleSupported`: capacidade.
- `GattServiceProvider.CreateAsync`, `GattLocalService.CreateCharacteristicAsync`
  e descriptors: HID Information, Report Map, HID Control Point, relatórios e
  Report Reference; Protocol Mode/boot reports conforme o descriptor escolhido.
- Proteção criptografada dos reports e bonding pelo Windows; CCCD notificável
  gerado pelo sistema.
- `StartAdvertising` com `IsConnectable` e `IsDiscoverable`, observando status e
  erro real; `StopAdvertising` no encerramento.
- `ReadRequested`, `WriteRequested`, `SubscribedClientsChanged`,
  `NotifyValueAsync`, sessão GATT e resultado por cliente para diagnosticar uso.
- `DeviceInformation`/pairing APIs para dispositivos conhecidos quando aplicável.
  A listagem genérica de um iPhone não prova conexão HOGP. No fluxo inicial
  recomendado, o usuário seleciona o PC anunciado nos ajustes do iPhone;
  o iMirror identifica a sessão/subscriber e restringe input ao host escolhido.
  Não prometer descoberta ativa de qualquer iPhone ou pareamento sem consentimento.

WPF pode acessar WinRT com
[TFM Windows versionado](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps).
Hoje `iMirror.Bluetooth` é apenas um stub `net10.0`, e `iMirror.Input` não tem
captura. A implementação futura precisará da referência Windows SDK e de um
TFM compatível também no app consumidor. Não impor Windows 11 quando este PC
é Windows 10; a consulta de capacidades e a publicação real serão os gates.
Apps empacotados declaram capability `bluetooth`; para o desktop sem pacote,
validar o probe .NET 10 efetivo antes de decidir por MSIX.

## Evidência e limites do iOS 27

O [guia atual de AssistiveTouch](https://support.apple.com/en-lamr/guide/iphone/iph96b21954/ios)
identifica iOS 27 e documenta mouse/trackpad Bluetooth e USB, pareamento,
botões e sensibilidade. O [guia de teclado](https://support.apple.com/en-lamr/guide/iphone/ipha4375873f/ios)
documenta teclado externo e Full Keyboard Access. A Apple também explica o
[pareamento de apontadores](https://support.apple.com/en-us/111775).
AssistiveTouch é a rota documentada para este teste. Full Keyboard Access é
opcional para navegação; digitação ainda depende do campo com foco e do layout.

O projeto original [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid)
relata teclado e mouse em iPhone com AssistiveTouch; reconexão no iPhone está
não testada. Isso é evidência de implementação possível, não certificação para
iPhone 14/iOS 27.0.1/driver Realtek. Nenhum código ou binário dele foi integrado.
Seu descriptor usa teclado e mouse **relativo** com wheel.

Limitações a validar fisicamente:

- Pairing, criptografia, leitura do Report Map e subscriptions separadas para
  teclado e mouse. “Pareado” ou “notify enviado” não prova efeito na tela.
- Clique, movimento, wheel, drag, teclas, layout português e modificadores.
  HID transporta usages/estados de teclas, não texto Unicode arbitrário.
- Reconexão após perda de link e reinício do processo, cache GATT e liberação
  de teclas/botões. Contagem de subscribers sozinha não prova link vivo.
- Mouse não é uma API de injeção de toque; gestos multitouch arbitrários não
  fazem parte dos reports básicos de teclado/mouse.
- Não foi encontrada nas fontes consultadas uma garantia de descriptor de
  ponteiro absoluto para esse iPhone. Não tratar digitizer absoluto como fallback
  aprovado sem testar sua aceitação e efeito no iOS.

## Captura e coordenadas, sem alterar o renderer

A janela externa pertence ao processo nativo; eventos WPF na janela principal
não capturam seus cliques. A integração futura pode identificar o HWND da janela
de vídeo pertencente ao receiver e usar captura Win32 restrita àquela janela,
sem injetar DLL, reparenting ou modificar UxPlay. Hooks de baixo nível, se usados,
devem encaminhar somente quando o HWND estiver em primeiro plano, o cursor
estiver na área renderizada e o modo de controle estiver explicitamente ativo.
ESC sai localmente; perda de foco/link e encerramento liberam estados. Não
registrar conteúdo digitado.

Para stream orientado `Ws × Hs` e área cliente `Wc × Hc`, em pixels físicos:

```
s = min(Wc / Ws, Hc / Hs)
ox = (Wc - s * Ws) / 2
oy = (Hc - s * Hs) / 2
xs = (xc - ox) / s
ys = (yc - oy) / s
```

Ignorar barras fora do retângulo do vídeo. Usar client rect, DPI correto e
dimensões/orientação atuais do stream; recalcular ao redimensionar/rotacionar e
não aplicar uma segunda rotação quando a imagem já vier orientada. Se o sink
tiver outro viewport/transformação, medir essa área em vez de presumir a fórmula.

Esse mapper calcula o **alvo geométrico**, não a posição atual do cursor iOS.
HID relativo envia `dx/dy`; sensibilidade/aceleração, saturação nas bordas e
posição inicial impedem transformar `xs/ys` diretamente em clique absoluto
confiável. Primeiro oferecer movimento relativo e clique no cursor observado no
vídeo. “Clicar diretamente no ponto espelhado” precisa de uma prova adicional
de ponteiro absoluto aceito ou de estratégia de sincronização validada. Não
prometer precisão por simples regra de três ou deslocamento fixo até uma borda.

## Sequência recomendada

1. Com Bluetooth ligado, probe isolado .NET 10: criar HID, anunciar, verificar
   status e interromper/limpar ao encerrar; sem hooks globais ou alteração AirPlay.
2. Parear pelo iPhone com AssistiveTouch, verificar Report Map, bonding e
   subscriptions. Enviar somente eventos de teste explícitos com release.
3. Validar movimento relativo, clique, scroll e teclado no aparelho, além de
   reconexão. Só então integrar controles à UI e captura restrita ao vídeo.
4. Testar o mapper com portrait/landscape, barras e DPI; validar separadamente
   qualquer promessa de posicionamento absoluto.

Se o transporte nativo falhar de forma reproduzível com rádio ligado, a
alternativa próxima é PC → USB/serial → microcontrolador → BLE HID → iPhone.
A Nordic fornece exemplos reais de
[mouse HIDS](https://github.com/nrfconnect/sdk-nrf/tree/main/samples/bluetooth/peripheral_hids_mouse).
Firmware composto keyboard/mouse e protocolo serial seriam trabalho adicional,
com o mesmo pareamento/AssistiveTouch e limite relativo. Não comprar hardware
agora: este Realtek já declara peripheral. Mouse/teclado físicos Bluetooth/USB
são alternativa documentada pela Apple, mas não dão automaticamente controle
pelo mouse e teclado do PC.

Status: **PHASE 3: FEASIBILITY INVESTIGATED — NATIVE HOGP PROTOTYPE PENDING**.
AirPlay, UxPlay, Bonjour, firewall, rede e renderer preservados.
