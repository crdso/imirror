# Investigação da conexão Bluetooth — 2026-10-07

**Estado: PENDING LIVE IPHONE CONNECTION DIAGNOSIS.** A conexão física relatada nesta sessão ainda não foi reproduzida com os novos observadores. As validações físicas anteriores de transporte/input são históricas; não comprovam a reconexão atual.

## Evidência e limites

- A sessão funcional de 2026-10-06 às 20:45 registrou GATT ativo, leitura de HIDInformation/ReportMap, bond e subscriptions de teclado/mouse; releases neutros tiveram Notify Success.
- A sessão problemática de 2026-10-07 às 06:21–06:25 criou o provider e anunciou Started, mas não registrou leituras HID nem subscribers. Connect repetido reutilizou geração 1. AirPlay continuou recebendo vídeo.
- O usuário conectou em Ajustes → Bluetooth: o iPhone mostrou conectado, sem retorno no PC, e depois exibiu outra entrada. Os logs existentes não identificam qual entrada/transporte foi selecionado.
- Os callbacks HID antigos só observam sessões após leitura/escrita/subscription de características. Sua ausência não prova ausência de link de rádio e não distingue cache, entrada Classic, segurança ou falha anterior às leituras criptografadas. Nenhuma dessas hipóteses está confirmada.
- Com o app fechado, a enumeração Windows encontrou dois vínculos Classic pareados/desconectados e nenhum endpoint BLE conhecido. É um retrato de associações conhecidas; não uma captura de todos os links ACL do rádio. Adaptador Realtek, rádio ligado, serviços Bluetooth em execução.

## Correções implementadas

Código: `6535f30 fix(bluetooth): expose Windows links before HID callbacks`.

- Dois DeviceWatchers independentes mostram associações Classic/BLE, bond e link do Windows mesmo antes de callbacks HID. A UI mantém esses dados separados de GATT, ReportMap e subscriptions. Link/bond não habilitam input nem selecionam o destino.
- Nomes aparecem somente na UI local; logs persistem aliases locais e estados, sem nomes, IDs Windows, MACs ou payloads de teclas. A observação não pareia, consulta GATT remoto, reinicia rádio ou altera bonds.
- A espera expirada informa a etapa realmente observada. Consultas incompletas/indisponíveis e link desconhecido não viram sucesso.
- Corrigido o filtro normal de diagnóstico: mensagens INFO do renderer parseado inundavam o painel com PTS. Verbose conserva os detalhes; warnings/errors relevantes continuam visíveis.
- `iMirror.exe --start-bluetooth` abre Controle e inicia o mesmo provider. Não inicia AirPlay nem captura/envia input automaticamente. Startup normal permanece manual.

Arquivos de produção: App.xaml.cs, DiagnosticVisibility.cs, MainViewModel.cs, MainViewModel.Presentation.cs e Views/ControlView.xaml em `src/iMirror.App`; BluetoothContracts.cs, BluetoothController.cs, WindowsBluetoothObservation.cs e iMirror.Bluetooth.csproj em `src/iMirror.Bluetooth`. Testes: Phase1/FinalPolishProbe.cs, Phase1/Program.cs e Phase3/Program.cs.

Provider, Report Map, proteção criptografada, ports, renderer e runtime AirPlay foram preservados. Nenhuma mudança de firewall, Bonjour, WSL, Wi-Fi ou driver. Não foi aplicado reset de rádio, remoção de bonds, publisher Appearance paralelo ou retry automático de provider.

## Validação local

- Debug e Release: 59/59 grupos por configuração (UI 10, AirPlay 10, BLE/input 39), 0 warnings/0 errors. Probe isolado: 11/11; fixtures de segurança de rede: 6/6, sem mudanças de rede.
- Observadores nativos Windows: PASS, enumeração completa; nenhum provider HID criado no teste, nenhum advertising/pareamento/input.
- Cursor/hooks/GStreamer reais com HID fake: PASS. Releases e desconexão removem captura. Isso não valida input no iPhone nesta sessão.
- Smoke dos dois pacotes: execução direta, ícones SMALL/BIG/Shell e atalho, geometria e fechamento exit 0; 142 hashes nativos preservados.
- Evidências locais ignoradas: `logs/bluetooth-followup-debug.log`, `logs/bluetooth-followup-final-debug-build.log`, `logs/bluetooth-followup-release.log`, `logs/bluetooth-native-associations-test.log`, `logs/bluetooth-followup-release-smoke.log`. Tentativa física: `logs/bluetooth-control.log`.

Pacotes locais deste código:

| Artefato | Bytes | SHA256 |
|---|---:|---|
| dist/iMirror/iMirror.exe | 27.362.528 | `476E1E81259C2F9375EADCEE619ECCEFEAFFAC945BADD3E0A2D46A9D4380FB5E` |
| dist/iMirror-Portable.zip | 151.809.742 | `8E648814E282AE1B8D1E40980DDD6A621C75C6ABE8DA0BCCDC5C76F6435E1237` |

FDD completo: 211.398.655 bytes; ZIP self-contained descompactado: 383.736.479 bytes; runtime AirPlay: 184.030.660 bytes. Binários, logs brutos e caches não são versionados.

## Próxima tentativa física

Manter uma única instância do iMirror em Controle, com anúncio iniciado. No iPhone, abrir Ajustes → Acessibilidade → Toque → AssistiveTouch → Dispositivos → Dispositivos Bluetooth; selecionar o PC uma vez e aguardar 30 segundos. Este é o caminho documentado pela [Apple para apontadores Bluetooth](https://support.apple.com/en-us/111775). Não concluir que a conexão anterior estava errada apenas por ter usado outro menu.

Reportar: apareceu nessa lista? O Windows observou link Classic ou BLE? GATT, HIDInformation, ReportMap, teclado e mouse avançaram? Apareceu segunda entrada? Não alternar rádio ou apagar todos os dispositivos durante a coleta. Se continuar sem callbacks, a investigação deve alcançar a etapa de link/segurança, sem atribuir sucesso HID ao rótulo Conectado do iPhone.

Referências: [Windows GATT server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server), [DeviceWatcher/cliente BLE](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client), [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid). O upstream não comprova reconexão iOS após reinício do app; não se extrapolam workarounds de cache Android para iOS.
