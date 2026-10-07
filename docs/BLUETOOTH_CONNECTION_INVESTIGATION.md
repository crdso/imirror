# Investigação da conexão Bluetooth — 2026-10-07

**Registro histórico, substituído pela revalidação iOS Stable.** O próximo teste e o rollback controlado estão em [IOS_STABLE_HID_REVALIDATION.md](IOS_STABLE_HID_REVALIDATION.md). As seções abaixo descrevem versões anteriores e não orientam a recuperação atual.

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

O usuário relatou desconexão rápida após o iPhone mostrar Conectado, com duas entradas do PC; a imagem mostrou nenhuma etapa GATT/HID. Na sessão das 13:55, o provider permaneceu na geração 1, sem mudança de rádio/advertising, e nenhuma leitura chegou ao app. O encerramento às 14:05 foi do aplicativo completo, com cleanup; não foi um timeout automático de pareamento. Uma imagem posterior confirmou o PC na lista do AssistiveTouch; o usuário já esqueceu ambas as entradas e relatou que voltou a mostrar conectado e depois caiu. Não há motivo para repetir remoção de bonds como diagnóstico.

A comparação de `HogpPeripheral` com o checkpoint anterior ao polimento não mostrou mudança do esquema/criptografia/publicação inicial. Não foi encontrada causa comprovada que justifique alterar transporte ou segurança. Ausência de callbacks permanece insuficiente para atribuir o problema ao iOS, driver, cache ou entrada Classic.

Foi acrescentado um diagnóstico separado, sem integração/admin obrigatório no startup WPF:

- `Diagnosticar-Bluetooth.cmd`: um clique solicita UAC e inicia uma coleta de 90 segundos em segundo plano. Se não houver iMirror em execução, aguarda confirmação do coletor e só então abre o EXE Release em Controle/Bluetooth. O iMirror é aberto pelo token original do launcher, sem herdar a elevação; permanece aberto/anunciando. O script não reinicia uma instância existente nem envia input.
- `tools/open-bluetooth-link-trace.ps1`: launcher com elevação fornecida pelo Windows; não trata credenciais. `-ShowConsole` é opcional.
- `tools/trace-bluetooth-link.ps1`: duração limitada a 180 segundos; mutex impede coletas concorrentes; heartbeat; cleanup normal em finally. Log em `logs/bluetooth-link-trace.log`.
- `tools/BluetoothLinkTrace.cs`: consumidor ETW x64 em tempo real, sem ETL. Permite somente campos numéricos de ConnectionComplete Classic/LE, AuthenticationComplete, EncryptionChange/KeyRefresh, SimplePairingComplete e DisconnectionComplete. Aliases L1/L2 identificam handles locais desta coleta; não identificam automaticamente o iPhone.

As 12 fixtures sem hardware do decodificador também foram incluídas em `tools/test.ps1`; não exigem administrador, advertising ou input.

Nenhum endereço, nome remoto/local, chave, conteúdo SMP/ATT ou payload HID é persistido. Somente metadados explícitos de anúncio e códigos de erro de protocolo são permitidos além dos eventos do controlador. As demais respostas/comandos/dados são descartados; prefixes permitidos são transitórios em memória e limpos após decodificação. Eventos abrangem todo o rádio; timestamps precisam coincidir com a tentativa física. Ausência de eventos em coleta interrompida não comprova ausência de conexão.

Validação: 12 fixtures passaram no PowerShell 7 e Windows PowerShell 5.1, incluindo conexão LE aprimorada, motivo de timeout, rejeição de chave/entrada truncada. Uma coleta nativa de dois segundos iniciou/encerrou com Win32=0, zero perdas, zero erros de parse; observou um pacote de dados descartado, sem nova conexão física nesse intervalo. A coleta seguinte de três minutos foi interrompida sem registrar STOP/SUMMARY; não há conclusão de controlador para aquela tentativa. O launcher do modo oculto/recovery retornou falha de elevação; essa validação não foi aprovada. Debug e Release completos passaram 59/59, 0 errors/warnings; evidências em `logs/bluetooth-disconnect-debug.log` e `logs/bluetooth-disconnect-release.log`. O primeiro restore Debug no sandbox falhou na consulta de vulnerabilidades NuGet; a execução com acesso à fonte oficial passou, sem desabilitar auditoria.

Se o processo da coleta for encerrado à força, finally não é garantido. O log START identifica sua sessão; para encerrar somente ela, use `tools/open-bluetooth-link-trace.ps1 -StopSession <nome exato do START>`. O parâmetro aceita exclusivamente `iMirror.Bluetooth.Status.<32 caracteres hexadecimais>`. Não pare outras sessões ETW. O logger não configura arquivo/persistência nem modifica rádio, serviços, bonds, driver ou rede. A ferramenta administrativa é independente do WPF; o retorno visual e os novos pacotes estão descritos abaixo.

O coletor salva somente nome/timestamp da sua sessão em `logs/bluetooth-link-active.json`. Na execução seguinte elevada, recupera esse logger exato antes de criar outro; o mutex impede interromper um coletor vivo. O estado só é removido após parada confirmada. A sessão interrompida desta investigação foi recuperada às 14:45 com logmanExit=0; a coleta seguinte encerrou normalmente com zero perdas e removeu o estado local. O erro de interpretação descoberto nessa coleta é descrito abaixo; não foi declarada correção da conexão.

## Correção do retorno visual e release desta atualização

Código do pacote: `bce88de`. O título de Controle agora mostra **Sem resposta HID após 30 s · anúncio mantido** em vez de manter Aguardando iPhone indefinidamente. A orientação aparece acima das etapas, dentro da área inicialmente visível. Foi removida a afirmação de que AssistiveTouch seria necessário apenas para exibir o ponteiro; a orientação usa o caminho Apple de dispositivos Bluetooth apontadores. Nenhum estado GATT/subscriber é inferido.

`HogpPeripheral.Publish` registra `pairing-timeout` uma vez por expiração da janela visual, com advertising/GATT/keyboard/mouse observados. Não chama StopAdvertising/Dispose nem transforma ausência de callbacks em desconexão. A correção envolve `MainViewModel.Presentation.cs`, `Views/ControlView.xaml`, `HogpPeripheral.cs` e a verificação WPF em Phase1/Program.cs.

Debug/Release: 59/59 grupos mais 12 fixtures do controlador, 0 warnings/0 errors. Probe isolado 11/11, fixtures de rede 6/6. Cursor/renderer nativo com HID fake: PASS. Nova captura da página Controle foi inspecionada; ela usa estado simulado e não comprova conexão física. Evidências: `logs/bluetooth-disconnect-debug.log`, `logs/bluetooth-disconnect-release.log`, `logs/bluetooth-disconnect-ui-release.log` e `.cache/validation/20261007-142838-336/bluetooth-timeout-feedback.png`. Smoke dos dois EXEs/ZIP: PASS, fechamento exit 0, ícones/WorkArea/142 hashes nativos preservados (`logs/bluetooth-disconnect-release-smoke.log`).

| Pacote atual | Bytes | SHA256 |
|---|---:|---|
| dist/iMirror/iMirror.exe | 27.362.528 | `D46929A898E751C842737A3EBF47116CE482F079255FD1D6ACD7D1F7990957C3` |
| dist/iMirror-Portable.zip | 151.810.174 | `AD430034C9952ED1C3F8907CC545B22D4A334FA0164E3B8EB541976BC327C892` |

FDD completo: 211.398.655 bytes; self-contained descompactado: 383.736.479 bytes. Os pacotes contêm o retorno visual atualizado; a ferramenta administrativa separada fica na raiz/tools do projeto. Logs, screenshots, estado da coleta e binários continuam ignorados no Git. A elevação da coleta já foi autorizada pelo usuário; **PENDING LIVE IPHONE CONNECTION DIAGNOSIS** permanece.

Referências técnicas: [OpenTrace/EventRecordCallback](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_logfilew), manifesto do provedor instalado `Microsoft-Windows-BTH-BTHPORT`, e [consumidor BIP/H4 do Wireshark](https://raw.githubusercontent.com/wireshark/wireshark/master/extcap/etl.c). Nenhum código de captura/dump do Wireshark foi incorporado.

## Correção do coletor BIP e isolamento de validação

A coleta de 90 s das 14:45 contou 235 registros, porém o decodificador assumiu que BIP usava o valor H4 4 para eventos e descartou o tipo 2. Portanto o antigo contador `event=0` é inválido como evidência de ausência de conexão. Os 12 testes anteriores cobriam apenas HCI interno e não detectavam esse erro no envelope Windows.

A captura corrigida confirmou respostas HCI CommandComplete de advertising (0x2006/0x2008/0x200A), status 0x00, dentro de BIP tipo 2. `TdhGetProperty` confere independentemente BIP_Type/BIP_DataLen pelo manifesto instalado antes de decodificar. O log agora conta tipos BIP numéricos, sem rótulos incorretos ACL/SCO/H4; aceita somente tipo 2, comprimento exato e eventos/status explicitamente permitidos. Não formata respostas desconhecidas, endereços, chaves ou dados. Referência: [TDH, leitura de propriedade nomeada](https://learn.microsoft.com/en-us/windows/win32/api/tdh/nf-tdh-tdhgetproperty).

Uma leitura concorrente do log expôs outra falha do coletor: Add-Content reabria o arquivo e recebeu sharing violation, interrompendo aquela captura. A escrita agora conserva um handle compartilhado durante a coleta. A execução seguinte de 120 s encerrou às 14:58 com 106 registros, 50 status decodificados, zero perdas, zero erros de schema/parse e sem falha de escrita durante leituras concorrentes. Não capturou ConnectionComplete/DisconnectionComplete: esse intervalo incluiu o app fechado e depois reaberto, e não comprova cobertura da tentativa física relatada. Não se atribui a queda ao driver, iOS, cache ou criptografia.

Foram adicionadas 12 fixtures do envelope BIP, totalizando 24, incluindo tipo 2 correto, rejeição do tipo H4 4, dados parecidos com eventos, retorno desconhecido, chave e truncamento. Nenhuma fixture cria provider ou trace nativo.

A execução de testes com o app aberto revelou dois conflitos separados: startup do processo de teste ignorava o diretório isolado no log Bluetooth; e o controller fake usava o semaphore do publisher real. O startup agora respeita IMIRROR_LOG_DIRECTORY também para Bluetooth (subpasta bluetooth), e fixtures injetam um nome de lease exclusivo. O nome padrão da instância nativa, esquema HID, segurança, advertising e AirPlay permanecem iguais. Esses ajustes isolam a validação; não são apresentados como solução da desconexão física. O EXE/ZIP acima continua sendo o pacote bce88de; a alteração de isolamento está compilada nos outputs Debug/Release do source e não exige trocar o app para usar o coletor atualizado.

Validação após as correções: Debug e Release completos, 59/59 grupos por configuração, mais 24/24 fixtures do controlador; 0 warnings/0 errors. As falhas iniciais de teste não foram ignoradas nem testes removidos; foram corrigidos os dois conflitos de isolamento e executadas novamente as suítes completas. Evidências locais: `logs/bluetooth-bip-debug.log`, `logs/bluetooth-bip-release.log`, `logs/bluetooth-link-trace.log`. A conexão física desta sessão continua pendente de uma tentativa efetivamente coberta pelo coletor corrigido.

## Evidência da queda e teste A/B descartado

Durante a coleta completa de 180 s das 15:04, o usuário relatou que o PC aparecia conectado automaticamente e depois desconectava, sem interação. Às 15:05:31 o rádio registrou **DisconnectionComplete, status 0x00, reason 0x13 (Remote User Terminated Connection)**. O iMirror conservou geração 1/Started e nenhum callback HID; não executou cleanup nesse horário. A coleta encerrou normalmente: 482 registros, zero perdas/erros, um pacote de dados descartado. O link já existia antes do início; seu transporte/origem não foi capturado. O alias L1 não identifica automaticamente o iPhone. O motivo confirma encerramento remoto do link observado, mas não explica a causa nem prova falha de HID, bond ou driver.

O launcher passou a aguardar até 20 s por confirmação de startup da própria coleta antes de abrir o app ausente. A nova sequência foi validada nativamente: trace começou às 15:08:56; advertising do app iniciou às 15:08:58. Foram preservados o token normal do app e a proteção contra múltiplas instâncias; apenas o coletor usa UAC. Não reinicia app existente nem altera rádio.

O coletor agora permite somente metadados de comandos LE de anúncio (conectável, intervalo, canais, flags, presença dos UUIDs HID/BAS, enable), e respostas completas de erro SMP PairingFailed, ATT ErrorResponse e L2CAP ConnectionResponse. Não lê/formata endereços, nomes, chaves, dados arbitrários, notifications ou teclas. Não reconstitui fragmentos. Foram acrescentadas 16 fixtures de privacidade/comprimentos/canais: **40/40** no PowerShell 7 e 5.1. Anúncio nativo observado: ADV_IND conectável, canal 0x07, filtro 0x00, HID 0x1812 presente, flags 0x1A; comandos aceitos com status 0x00. Isso comprova configuração do controlador, não recepção pelo iPhone.

A captura também mostrou alternância com anúncio sem UUID HID. Como o upstream anuncia apenas HID, foi testada temporariamente a remoção do advertising BAS em um app compilado separado. A mesma alternância continuou (captura de 120 s às 15:20, 322 registros, zero perdas/erros). **Hipótese de BAS como causa da alternância não confirmada; alteração experimental totalmente desfeita.** O produtor desse outro anúncio ainda não foi identificado. Não se atribuem as duas entradas do iPhone a BAS. Nenhuma mudança desse teste é mantida na publicação HID, proteção ou esquema; o app de teste é encerrado normalmente e o pacote original preservado.

O status permanece **PENDING LIVE IPHONE CONNECTION DIAGNOSIS**. A instrumentação passou a capturar a queda; a conexão física ainda não foi corrigida ou revalidada.

Validação final após desfazer o experimento: Debug/Release, 59/59 grupos e 40/40 fixtures por configuração; 0 warnings/0 errors. Evidências: `logs/bluetooth-final-debug.log` e `logs/bluetooth-final-release.log`. As fontes e outputs finais conservam a publicação HID original; não ficou workaround de BAS, rádio ou segurança. UxPlay/configuração/perfil mantêm os hashes registrados anteriormente. O pacote bce88de continua preservado, e usa os scripts externos de diagnóstico atualizados.

## Parada explícita da espera Bluetooth

A coleta solicitada novamente começou às 15:46:08 e terminou às 15:49:08: 482 registros, 476 metadados decodificados, zero perdas/erros de schema/parse. Não registrou conexão ou tráfego de dados; o app permaneceu em geração 1, sem leituras HID/subscribers. O intervalo sozinho não comprova uma tentativa física do usuário.

Em Controle foi acrescentado **Parar Bluetooth**. Cancela a inicialização pendente, aguarda e libera qualquer captura em andamento, e solicita cleanup/StopAdvertising. Enquanto aguarda, Conectar fica desabilitado para não renovar inadvertidamente o pareamento. Parar não encerra AirPlay, desliga o rádio, apaga bonds ou altera rede/firewall. Uma parada confirmada permite uma conexão explícita posterior. A operação é serializada e idempotente.

O teste nativo separado das 15:53 criou um provider real e solicitou sua parada, sem enviar cliques/teclas ou alterar pareamento. O Windows ainda reportou AdvertisingStatus=Started após o prazo de confirmação. Portanto a UI usa **StopUnconfirmed**, bloqueia input e outra criação, retém o lease até o encerramento e informa **Feche e reabra o iMirror**. Não apresenta esse resultado como anúncio comprovadamente parado nem libera o lease em uma parada intermediária. Ao encerrar normalmente o app, o lease é liberado. O teste não valida a reconexão do iPhone.

Testes cobrem o botão/binding WPF, parada durante startup, preservação de AirPlay, bloqueio de ativação durante a parada, cliques repetidos, reconexão somente após parada confirmada e retenção do lease quando a parada nativa não é confirmada. A conexão física segue **PENDING LIVE IPHONE CONNECTION DIAGNOSIS**.

Validação final do código `036fb36`: Debug e Release **61/61 grupos** (11 UI, 10 AirPlay, 40 BLE/input), mais **40/40 fixtures** do coletor por configuração; **0 warnings/0 errors**. Evidências locais: `logs/bluetooth-stop-debug.log`, `logs/bluetooth-stop-release.log` e `logs/bluetooth-native-stop-waiting.log`. Logs/fixtures nativas de execução e estado temporário continuam ignorados no Git.

O teste nativo foi repetido na versão final Release às 16:01, com o mesmo resultado de parada não confirmada e rádio preservado como ligado. Os pacotes foram reabertos no smoke test; os 142 hashes nativos de cada variante, UxPlay 1.73.7, GStreamer 1.28.7, HEVC, renderer e portas originais foram conferidos. Evidência local: `logs/bluetooth-stop-package-smoke.log`. Esses pacotes substituem o pacote bce88de descrito historicamente acima, preservando seu runtime AirPlay.

| Pacote atualizado, código 036fb36 | Bytes | SHA-256 |
|---|---:|---|
| dist/iMirror/iMirror.exe | 27.366.624 | `9710D0BBF4BCC51074A77FCB7C0AD6F7C82A8D41A017703814A6127E2BE76E6C` |
| dist/iMirror-Portable.zip | 151.812.554 | `E79C5F54ECBEC5297C031F60380A7CC50743C607B766800B580044BC321822AD` |
