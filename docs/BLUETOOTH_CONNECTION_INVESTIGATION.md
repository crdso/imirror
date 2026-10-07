# Investigação da conexão Bluetooth — 2026-10-07

**Estado: PENDING LIVE IPHONE CONNECTION DIAGNOSIS.** A conexão HID desta sessão não foi validada e a causa da falha permanece sem confirmação. As validações físicas anteriores de transporte/input são históricas; não comprovam a reconexão atual.

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

Histórico dos pacotes do código `6535f30` (substituídos pelo release abaixo):

| Artefato | Bytes | SHA256 |
|---|---:|---|
| dist/iMirror/iMirror.exe | 27.362.528 | `476E1E81259C2F9375EADCEE619ECCEFEAFFAC945BADD3E0A2D46A9D4380FB5E` |
| dist/iMirror-Portable.zip | 151.809.742 | `8E648814E282AE1B8D1E40980DDD6A621C75C6ABE8DA0BCCDC5C76F6435E1237` |

FDD completo: 211.398.655 bytes; ZIP self-contained descompactado: 383.736.479 bytes; runtime AirPlay: 184.030.660 bytes. Binários, logs brutos e caches não são versionados.

## Próxima tentativa física

Manter uma única instância do iMirror em Controle, com anúncio iniciado. No iPhone, abrir Ajustes → Acessibilidade → Toque → AssistiveTouch → Dispositivos → Dispositivos Bluetooth; selecionar o PC uma vez e aguardar 30 segundos. Este é o caminho documentado pela [Apple para apontadores Bluetooth](https://support.apple.com/en-us/111775). Não concluir que a conexão anterior estava errada apenas por ter usado outro menu.

Reportar: apareceu nessa lista? O Windows observou link Classic ou BLE? GATT, HIDInformation, ReportMap, teclado e mouse avançaram? Apareceu segunda entrada? Não alternar rádio ou apagar todos os dispositivos durante a coleta. Se continuar sem callbacks, a investigação deve alcançar a etapa de link/segurança, sem atribuir sucesso HID ao rótulo Conectado do iPhone.

Referências: [Windows GATT server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server), [DeviceWatcher/cliente BLE](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client), [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid). O upstream não comprova reconexão iOS após reinício do app; não se extrapolam workarounds de cache Android para iOS.

## Atualização: desconexão espontânea e diagnóstico do controlador

O usuário relatou desconexão rápida após o iPhone mostrar Conectado, com duas entradas do PC; a imagem mostrou nenhuma etapa GATT/HID. Na sessão das 13:55, o provider permaneceu na geração 1, sem mudança de rádio/advertising, e nenhuma leitura chegou ao app. O encerramento às 14:05 foi do aplicativo completo, com cleanup; não foi um timeout automático de pareamento. O usuário informou que a tentativa pelo AssistiveTouch também não conectou ou não encontrou o PC; distinguir esses dois resultados ainda exige resposta.

A comparação de `HogpPeripheral` com o checkpoint anterior ao polimento não mostrou mudança do esquema/criptografia/publicação inicial. Não foi encontrada causa comprovada que justifique alterar transporte ou segurança. Ausência de callbacks permanece insuficiente para atribuir o problema ao iOS, driver, cache ou entrada Classic.

Foi acrescentado um diagnóstico separado, sem integração/admin obrigatório no startup WPF:

- `Diagnosticar-Bluetooth.cmd`: um clique abre o EXE Release em Controle/Bluetooth se não houver iMirror em execução, solicita UAC e inicia uma coleta de 90 segundos em segundo plano. O iMirror é aberto pelo token original, antes da elevação do coletor; permanece aberto/anunciando. O script não reinicia uma instância existente nem envia input.
- `tools/open-bluetooth-link-trace.ps1`: launcher com elevação fornecida pelo Windows; não trata credenciais. `-ShowConsole` é opcional.
- `tools/trace-bluetooth-link.ps1`: duração limitada a 180 segundos; mutex impede coletas concorrentes; heartbeat; cleanup normal em finally. Log em `logs/bluetooth-link-trace.log`.
- `tools/BluetoothLinkTrace.cs`: consumidor ETW x64 em tempo real, sem ETL. Permite somente campos numéricos de ConnectionComplete Classic/LE, AuthenticationComplete, EncryptionChange/KeyRefresh, SimplePairingComplete e DisconnectionComplete. Aliases L1/L2 identificam handles locais desta coleta; não identificam automaticamente o iPhone.

As 12 fixtures sem hardware do decodificador também foram incluídas em `tools/test.ps1`; não exigem administrador, advertising ou input.

Nenhum endereço, nome remoto, chave, SMP, conteúdo ATT ou payload HID é persistido. Comandos/pacotes de dados são descartados antes de cópia; os prefixes de eventos permitidos são transitórios em memória e limpos após decodificação. Eventos abrangem todo o rádio; timestamps precisam coincidir com a tentativa física. Ausência de eventos em coleta interrompida não comprova ausência de conexão.

Validação: 12 fixtures passaram no PowerShell 7 e Windows PowerShell 5.1, incluindo conexão LE aprimorada, motivo de timeout, rejeição de chave/entrada truncada. Uma coleta nativa de dois segundos iniciou/encerrou com Win32=0, zero perdas, zero erros de parse; observou um pacote de dados descartado, sem nova conexão física nesse intervalo. A coleta seguinte de três minutos foi interrompida sem registrar STOP/SUMMARY; não há conclusão de controlador para aquela tentativa. O launcher do modo oculto/recovery retornou falha de elevação; essa validação não foi aprovada. Debug e Release completos passaram 59/59, 0 errors/warnings; evidências em `logs/bluetooth-disconnect-debug.log` e `logs/bluetooth-disconnect-release.log`. O primeiro restore Debug no sandbox falhou na consulta de vulnerabilidades NuGet; a execução com acesso à fonte oficial passou, sem desabilitar auditoria.

Se o processo da coleta for encerrado à força, finally não é garantido. O log START identifica sua sessão; para encerrar somente ela, use `tools/open-bluetooth-link-trace.ps1 -StopSession <nome exato do START>`. O parâmetro aceita exclusivamente `iMirror.Bluetooth.Status.<32 caracteres hexadecimais>`. Não pare outras sessões ETW. O logger não configura arquivo/persistência nem modifica rádio, serviços, bonds, driver ou rede. A ferramenta administrativa é independente do WPF; o retorno visual e os novos pacotes estão descritos abaixo.

O coletor salva somente nome/timestamp da sua sessão em `logs/bluetooth-link-active.json`. Na execução seguinte elevada, recupera esse logger exato antes de criar outro; o mutex impede interromper um coletor vivo. O estado só é removido após parada confirmada. A sessão interrompida desta investigação foi identificada nesse estado local; sua parada direta recebeu Access Denied e a recuperação ainda exige UAC. O botão único permite realizar recuperação e nova coleta na mesma elevação. Não foi declarada coleta física concluída nem correção da conexão.

## Correção do retorno visual e release desta atualização

Código do pacote: `bce88de`. O título de Controle agora mostra **Sem resposta HID após 30 s · anúncio mantido** em vez de manter Aguardando iPhone indefinidamente. A orientação aparece acima das etapas, dentro da área inicialmente visível. Foi removida a afirmação de que AssistiveTouch seria necessário apenas para exibir o ponteiro; a orientação usa o caminho Apple de dispositivos Bluetooth apontadores. Nenhum estado GATT/subscriber é inferido.

`HogpPeripheral.Publish` registra `pairing-timeout` uma vez por expiração da janela visual, com advertising/GATT/keyboard/mouse observados. Não chama StopAdvertising/Dispose nem transforma ausência de callbacks em desconexão. A correção envolve `MainViewModel.Presentation.cs`, `Views/ControlView.xaml`, `HogpPeripheral.cs` e a verificação WPF em Phase1/Program.cs.

Debug/Release: 59/59 grupos mais 12 fixtures do controlador, 0 warnings/0 errors. Probe isolado 11/11, fixtures de rede 6/6. Cursor/renderer nativo com HID fake: PASS. Nova captura da página Controle foi inspecionada; ela usa estado simulado e não comprova conexão física. Evidências: `logs/bluetooth-disconnect-debug.log`, `logs/bluetooth-disconnect-release.log`, `logs/bluetooth-disconnect-ui-release.log` e `.cache/validation/20261007-142838-336/bluetooth-timeout-feedback.png`. Smoke dos dois EXEs/ZIP: PASS, fechamento exit 0, ícones/WorkArea/142 hashes nativos preservados (`logs/bluetooth-disconnect-release-smoke.log`).

| Pacote atual | Bytes | SHA256 |
|---|---:|---|
| dist/iMirror/iMirror.exe | 27.362.528 | `D46929A898E751C842737A3EBF47116CE482F079255FD1D6ACD7D1F7990957C3` |
| dist/iMirror-Portable.zip | 151.810.174 | `AD430034C9952ED1C3F8907CC545B22D4A334FA0164E3B8EB541976BC327C892` |

FDD completo: 211.398.655 bytes; self-contained descompactado: 383.736.479 bytes. Os pacotes contêm o retorno visual atualizado; a ferramenta administrativa separada fica na raiz/tools do projeto. Logs, screenshots, estado da coleta e binários continuam ignorados no Git. O usuário precisa autorizar o UAC para obter a próxima coleta; **PENDING LIVE IPHONE CONNECTION DIAGNOSIS** permanece.

Referências técnicas: [OpenTrace/EventRecordCallback](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_logfilew), manifesto do provedor instalado `Microsoft-Windows-BTH-BTHPORT`, e [consumidor BIP/H4 do Wireshark](https://raw.githubusercontent.com/wireshark/wireshark/master/extcap/etl.c). Nenhum código de captura/dump do Wireshark foi incorporado.
