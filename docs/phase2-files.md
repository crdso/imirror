# Arquivos da entrega parcial da fase 2

## Criados

| Arquivo | Função |
| --- | --- |
| airplay.json | Caminhos/opções do build portátil. |
| src/iMirror.AirPlay/AirPlayOptions.cs | Opções tipadas/validação. |
| src/iMirror.AirPlay/AirPlayConfiguration.cs | JSON e resolução relativa. |
| src/iMirror.AirPlay/AirPlayStatus.cs | Estados, erros, métricas e contrato. |
| src/iMirror.AirPlay/AirPlayDependencyService.cs | Checker PE x64/versões/plugins/Bonjour. |
| src/iMirror.AirPlay/CommandProbe.cs | Diagnósticos externos/timeout. |
| src/iMirror.AirPlay/UxPlayArguments.cs | Argumentos centrais sem shell. |
| src/iMirror.AirPlay/UxPlayLogParser.cs | Eventos da tag1.73.7. |
| src/iMirror.AirPlay/ReceiverProcess.cs | Processo externo, leitura e parada própria. |
| src/iMirror.AirPlay/UxPlayProcessService.cs | Lifecycle, lock, supervisão e limpeza. |
| src/iMirror.App/AsyncRelayCommand.cs | Comando assíncrono na thread WPF. |
| tests/iMirror.Phase2.Tests/iMirror.Phase2.Tests.csproj | Harness sem NuGet. |
| tests/iMirror.Phase2.Tests/Program.cs | 7 grupos e processo auxiliar real. |
| tools/build-uxplay.ps1 | Build nativo fixado/downloads locais anunciados. |
| tools/extract-bonjour.ps1 | Extração read-only do MSI/CAB x64. |
| tools/send-ctrl-c.ps1 | Helper console isolado, recusa terceiros. |
| tools/configure-airplay-firewall.ps1 | Prévia padrão; aplicação explícita. |
| third_party/uxplay-1.73.7-live-logs.patch | Patch externo stdout Windows. |
| third_party/UxPlay-LICENSE | Licença upstream. |
| docs/phase2-validation.md | Resultados e checklist físico pendente. |
| docs/video-integration.md | Gate e critérios da etapa posterior. |
| docs/phase2-files.md | Este inventário. |
| docs/images/phase2-window.png | Preview UI em fixture, não vídeo. |
| docs/images/phase2-window-minimum.png | Preview mínimo UI em fixture. |
| docs/images/phase2-native-dependencies.png | Preview Bonjour ausente real. |

## Modificados

| Arquivo | Mudança |
| --- | --- |
| src/iMirror.App/MainViewModel.cs | Receiver/eventos/estados/métricas/shutdown. |
| src/iMirror.App/MainWindow.xaml | Botão/status AirPlay, métricas e janela externa. |
| src/iMirror.App/MainWindow.xaml.cs | Fechamento assíncrono com limpeza. |
| src/iMirror.App/App.xaml.cs | Composição/configuração/cleanup em falha. |
| src/iMirror.App/iMirror.App.csproj | Helper de parada no output. |
| tests/iMirror.Phase1.Tests/Program.cs | Regressões da base, botão real e previews atuais. |
| tools/test.ps1 | Executa ambos harnesses. |
| tools/run.ps1 | Seleciona configuração deste projeto. |
| iMirror.sln | Projeto de testes fase2. |
| README.md | Instalação explícita, uso, firewall/diagnóstico. |
| docs/phase2-plan.md | Plano1.73.7/gate/estados. |
| docs/architecture.md | Nota direcionando desenho histórico à implementação atual. |
| docs/files.md | Nota do inventário histórico fase1. |
| third_party/README.md | Origens/versões/hashes/fonte/licenças/patch. |

Removido: src/iMirror.AirPlay/PhaseOneAirPlayService.cs, stub substituído.
Bluetooth/Input e suas interfaces arquiteturais não modificados.

## Incremento de preparação sem iPhone

| Arquivo | Função |
| --- | --- |
| tools/airplay-setup-common.ps1 | Assinatura/MSI, PE x64, regras exatas, processos diagnósticos limitados e fingerprint. |
| tools/finish-airplay-setup-admin.ps1 | Somente instalação Bonjour, serviço e firewall com administrador. |
| tools/airplay-doctor.ps1 | Diagnóstico real, log/JSON e exit code 0 pronto / 1 pendente ou erro. |
| tools/prepare-airplay.ps1 | Preparação idempotente, builds/testes, doctor e launcher. |
| tools/start-imirror.ps1 | Abre Release pelo runtime local; reconhece instância própria já aberta. |
| tests/iMirror.Phase1.Tests/AirPlayReadinessProbe.cs | Cinco ciclos pelo comando WPF real, stdout/stderr, browse local e limpeza. Não usa receiver fictício. |
| docs/READY_FOR_IPHONE.md | Roteiro curto com a etapa administrativa restante e teste físico. |
| logs/airplay-readiness.log e .json | Resultados sanitizados da máquina; arquivos locais gerados. |
| iMirror.lnk | Atalho local gerado para abrir Release sem PowerShell manual. |

Atualizados: checker com rtph264depay/decodebin, timeout configurável do probe
diagnóstico, entrada do harness WPF, helper de parada no output de testes,
extração do browser dns-sd x64, firewall restrito, UTF-8 nos scripts, README/plano/
validação e ignore do atalho gerado. Nenhum teste existente removido.

## Preparação local ignorada

Incremento iOS27: `profiles/UxPlay-iOS27.json`, `tools/select-airplay-profile.ps1`,
`NegotiationLogFilter.cs`, `AirPlayNegotiationTrace.cs`, `MirrorTcpInspection.cs`,
`AirPlayAttemptLog.cs` e `docs/IOS27_AIRPLAY_TEST.md`. Atualizados argumentos/opções,
resolução de configuração, checker HEVC, parser/categorias, supervisão/logs,
launcher, harnesses e evidência de build. Não alterados: UxPlay.exe 1.73.7,
airplay.json, firewall e módulos Bluetooth/Input.

`.tools/msys64`, `.tools/bonjour-sdk`, `.tools/bonjour-cab`, MSI Bonjour64, pacote
portátil MSYS2 e `.tools/msys2-packages.txt`. Fonte em `.cache/research/UxPlay-1.73.7`,
build em `.cache/uxplay-build`, evidências em `.cache/validation`. `.tools/dotnet`
já existia da fase1. Nenhum serviço Bonjour/regra de firewall instalado nesses
preparativos. Esses diretórios não constituem pacote de redistribuição.
