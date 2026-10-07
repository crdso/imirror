# Validação fase 2 — preparação administrativa e teste físico pendentes

## Atualização: tentativa física iOS 27.0.1

O usuário reportou discovery normal e duas tentativas de conexão rejeitadas.
Discovery foi observado; vídeo, rotação e reconexão permanecem pendentes.
Bonjour agora está Running/Automatic. Próximo perfil: UxPlay 1.73.7 com -h265,
sem substituir o binário, renderer ou firewall. Investigação e critérios de log
em [IOS27_AIRPLAY_TEST.md](IOS27_AIRPLAY_TEST.md).
Suíte atual: 5 grupos WPF + 8 grupos AirPlay por configuração, sem remoção de testes.

## Histórico da preparação inicial

Data: 2026-10-05. Windows10 x64 19045, SDK10.0.401, runtime10.0.12.
UxPlay1.73.7, commit df67c212a433cf6dda3676dd40c097900d24e645, patch externo stdout.
GStreamer1.28.7 UCRT64 x64, avdec_h264 + d3d11videosink, sem áudio.

## Resultados automáticos

| Verificação | Resultado |
| --- | --- |
| Debug / Release | Ambos: 0 erros, 0 avisos nos projetos C#. |
| Harness WPF/logs/processo | 5 grupos passaram por configuração. |
| Harness AirPlay | 7 grupos passaram por configuração. |
| Build UxPlay | Concluído; -v retorna1.73.7. Warnings upstream apareceram na compilação nativa; nenhuma função de protocolo foi alterada. |
| gst-inspect / gst-launch | Ambos 1.28.7 x64; plugins appsrc/h264parse/videoconvert/rtph264depay/decodebin/avdec_h264/d3d11videosink carregados. |
| Pipeline sintético | videotestsrc num-buffers=60 → video/x-raw 640×360 30fps → videoconvert → d3d11videosink; janela aberta, EOS, exit0, rendered60/dropped0. Apenas renderer local; não comprova vídeo AirPlay. |
| Bonjour MSI | SHA256 conferido, Authenticode Valid, Publisher Apple Inc., ProductVersion3.1.0.1. Tentativa silenciosa única retornou1603 por privilégios insuficientes; serviço não instalado. |
| Checker real | ServiceDiscoveryMissing: Bonjour Service ausente. UxPlay/GStreamer/plugins foram validados antes desse resultado. |
| Botão real | Mostra erro amigável, libera nova tentativa, mantém interface utilizável. |
| UDP5353 | Bind compartilhado IPv4/IPv6 passou. Chrome/Spotify usam mDNS; isso isoladamente não indica conflito. Nenhum endpoint Bonjour, pois serviço ausente. Não aprova registro mDNS. |
| Firewall | Três regras exatas preparadas: UxPlay TCP/UDP35000–35002 e Bonjour UDP5353, Private/LocalSubnet, por executável. Aplicação bloqueada por falta de administrador. |
| Argumentos UxPlay real | Executável inicia com argumentos centrais e nome iMirror - Windows; termina no erro DNS-SD -65563. Exit0 upstream não significa receiver pronto. |
| Cinco ciclos reais via iMirror | 0/5, BLOCKED por Bonjour ausente. Harness preparado usa comando WPF real, stdout/stderr, duplicatas, browse local e limpeza. Não substituído por fixture. |
| AirPlay Doctor | Exit1: NOT READY: ADMINISTRATIVE SETUP REQUIRED. Log sanitizado em logs/airplay-readiness.log; JSON em logs/airplay-readiness.json. |
| Preparação idempotente | Executada repetidamente, incluindo PowerShell5.1 padrão do Windows; builds/testes passam, não repete instalação/UAC bloqueados nem cria regras duplicadas. |
| Teste físico / anúncio local | Não aprovado; Bonjour ausente. Discovery físico, vídeo AirPlay, rotação e reconexão continuam NOT TESTED. |

Comandos: `tools/test.ps1 -Configuration Debug` e `-Configuration Release`.
Evidências atuais: `.cache/readiness/validation-Debug.txt` e `validation-Release.txt`.
Resultados/fingerprint: `.cache/airplay-validation.json`; pastas timestampadas
dos harnesses são identificadas nos arquivos de saída acima.
Pastas contêm logs, dependencies.json, previews UI e logs do executável real.

Cobertura: persistência/limite de logs, falha de caminho, bindings/F11/ESC/restauração,
botão com diagnóstico real e abertura/fechamento do executável; argumentos/config relativa,
PE x64/x86/ausente, PATH, versões/plugins/serviço, parsing e métricas, partidas duplicadas,
lock entre instâncias, reset/reconexão de estado, início com erro, timeout/cancelamento,
saída inesperada/exit code e parada graciosa simulada. Processo auxiliar real verifica
stdout/stderr, fallback kill e preservação de outro filho. Não simula AirPlay inteiro.

Dois bugs WPF encontrados e corrigidos: Close reentrante após cleanup síncrono e
CanExecuteChanged fora da thread UI no diagnóstico. Resultados acima são finais,
após as correções.

## Critérios

- [x] Debug/Release sem erros; testes automatizados passam.
- [x] Dependências detectadas e instalação incompleta reportada.
- [x] Supervisão/captura/parsing implementados e testados com processo auxiliar.
- [ ] UxPlay real inicia/anuncia pelo iMirror — requer Bonjour instalado.
- [ ] iPhone encontra iMirror - Windows e conecta.
- [ ] Tela real aparece/atualiza na janela externa.
- [ ] Desconexão física e logs da sessão real.
- [ ] Reconexão física, rotação, lock/unlock e interrupção de rede.
- [ ] Parar/fechar com receiver real deixa zero processos próprios residuais.

**PENDING PHYSICAL IPHONE VALIDATION. Não considerar fase 2 concluída.**
`READY FOR IPHONE TEST` só será emitido após administrador instalar/iniciar Bonjour,
aplicar firewall e o doctor aprovar cinco ciclos reais sem iPhone.
Investigação/protótipo interno aguardam gate;
nenhum trabalho de Bluetooth/Input da fase3 iniciado.

## Roteiro manual

Executar `tools/finish-airplay-setup-admin.ps1` como administrador, depois
`tools/prepare-airplay.ps1` normalmente. Após doctor aprovado, abrir iMirror.lnk
→ Iniciar AirPlay → Aguardando iPhone, na mesma LAN privada. No iPhone: Central
de Controle → Espelhamento de Tela → iMirror - Windows.
Roteiro curto: [READY_FOR_IPHONE.md](READY_FOR_IPHONE.md).

| Caso | Resultado esperado | Resultado a preencher |
| --- | --- | --- |
| Descoberta | Nome exato aparece; registrar demora e log de anúncio. | Pendente |
| Conexão | Conectando/conectado/streaming; tela real em janela externa. | Pendente |
| Retrato/paisagem/retrato | App com rotação habilitada; dimensões mudam, sem distorção/travamento. | Pendente |
| Desconexão | Parar Espelhamento no iPhone; estado desconectado, app aberto. | Pendente |
| Reconexão | Três ciclos sem reiniciar iMirror. | Pendente |
| Bloquear/desbloquear | Registrar comportamento iOS, retorno do vídeo e logs. | Pendente |
| Interrupção de rede | Desligar/religar Wi-Fi brevemente; registrar atraso e reconexão. | Pendente |
| Parar/iniciar receiver | Somente receiver encerra; novo início permite anúncio. | Pendente |
| Fechar app ativo | Sem uxplay.exe próprio residual; não matar outros receivers para mascarar resultado. | Pendente |

Se falhar, registrar horário e linhas relevantes dos logs. Não avançar para vídeo
interno até corrigir milestone externo. PID/status/pipeline sintético/fixtures não
substituem confirmação física.

Retornar modelo iPhone, versão iOS, Windows/rede, descoberta sim/não, vídeo sim/não,
orientação, ciclos, lock/unlock/rede, erros e arquivo de log indicado na UI.

## Métricas

Receiver: stopwatch do início do processo ao log de anúncio. Conexão: solicitação
identificada ao primeiro log de streaming. Resolução: wxh do renderer. Sem sessão real,
esses campos são N/A. FPS/latência fim a fim: N/A. Para latência futura, filmar
cronômetro/ação no iPhone e na tela PC na mesma tomada, registrar taxa da câmera e
comparar vários frames. Tempo de conexão não é latência de vídeo. Não há captura de
tela usada como renderer.
