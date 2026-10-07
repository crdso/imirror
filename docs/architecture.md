# Arquitetura — fase 1

Este documento preserva a base aprovada. Na fase2 o stub AirPlay foi substituído por
IAirPlayReceiver/UxPlayProcessService e checker x64; consulte [plano atual](phase2-plan.md)
e [inventário](phase2-files.md). Bluetooth/Input permanecem no desenho original.
Vídeo interno aguarda teste externo.

## Decisão: C# + WPF + .NET 10 LTS

WPF tem ciclo de vida desktop simples, binding XAML, Dispatcher, input por janela
e integração com HWND/HwndHost. Para a base pedida, dispensa dependência Windows
App SDK e bootstrap de pacote. A escolha prioriza estabilidade operacional e
integração de vídeo nativo; não exige uma interface sofisticada nesta etapa.
[Visão geral WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/).

WinUI 3 também atende Windows 10/11 e é a recomendação atual da Microsoft para
interfaces novas, distribuída no Windows App SDK. Seria uma alternativa adequada
para Fluent Design, mas acrescenta componentes que esta base não precisa.
Esta comparação é uma decisão de engenharia do projeto, não uma afirmação de
que WinUI é instável. [Visão geral WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/).

.NET 10 foi escolhido pelo horizonte LTS até novembro de 2028; .NET 8 chega ao
fim de suporte em novembro de 2026. `global.json` aceita SDK 10.0 estável com
roll-forward de feature band. [Ciclo .NET](https://learn.microsoft.com/dotnet/core/releases-and-support).

A base usa `net10.0-windows` no app/testes e `net10.0` nas bibliotecas sem API
Windows. Na fase 3, apenas os projetos que precisarem de WinRT receberão TFM
versionado, por exemplo `net10.0-windows10.0.22000.0`, e mínimo 19041, com guardas
para APIs Windows 11. Isso exige configurar origem NuGet para os SDK reference
packs então necessários. [WinRT desktop](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps).

Alvo técnico pretendido: Windows 10 2004+ e Windows 11; validação atual somente
no Windows local. Compatibilidade de execução e suporte oficial de sistema
operacional são distintos. A matriz atual da Microsoft limita Windows 10 a
edições LTSC/Enterprise indicadas; recomendar Windows 11 suportado para produção.
[Instalação e matriz .NET/Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Projetos e dependências

```text
iMirror.App (WPF, composição, MainViewModel, janela)
  ├── iMirror.Core (interfaces de disponibilidade e diagnósticos)
  ├── iMirror.AirPlay ────→ Core
  ├── iMirror.Bluetooth ──→ Core
  └── iMirror.Input ──────→ Core (reservado; sem captura)

iMirror.Phase1.Tests → App → módulos
```

- **Core:** `IFeatureService`, `FeatureStatus`, `IDiagnosticLog`, `LogEntry` e
  `FileDiagnosticLog`. Não referencia WPF, Bluetooth ou protocolo AirPlay.
- **App:** compõe serviços em `App.OnStartup`; `MainViewModel` oferece comandos,
  disponibilidade e coleção somente leitura de logs. A janela mantém os detalhes
  de fullscreen e eventos locais de teclado. Sem framework MVVM/DI adicional.
- **AirPlay:** `PhaseOneAirPlayService` informa ausência da implementação;
  não busca nem inicia UxPlay. A fase 2 substituirá/adaptará essa disponibilidade
  e adicionará contratos de sessão conforme o backend real.
- **Bluetooth:** `PhaseOneBluetoothService` informa ausência da implementação;
  não consulta rádio, cria GATT, pareia ou envia relatórios.
- **Input:** assembly vazio preparado na solução; nenhuma API/hook de captura.
- **Tests:** executável STA com verificações de persistência, concorrência,
  janela/binding, comandos e abertura/encerramento do processo real.

`FeatureStatus.IsConnected` é sempre false nesta fase; a UI nunca simula conexão.
Não se define antecipadamente uma API de frames ou HID que não foi validada.
Os botões de disponibilidade mantêm a janela utilizável e geram aviso no painel.

## Logs e erros

Logs UTF-8 por sessão em `%LOCALAPPDATA%\iMirror\logs`, nome com data e GUID,
timestamp com offset, nível e origem. `IMIRROR_LOG_DIRECTORY` permite direcionar
evidências de testes para o workspace. Caminho aparece abaixo do painel.

Histórico em memória/UI limitado a 500 registros; arquivo guarda todos os eventos
da sessão. Escritas são serializadas e liberadas em disco a cada entrada. Eventos
oriundos de outras threads chegam à UI via Dispatcher. Fechamento libera observadores.
Fase 1 gera poucos eventos; um backend de vídeo/input deverá usar logs agregados
e, se necessário, uma fila de escrita limitada. Não registrar teclas/texto digitado.
Não há rotação automática de arquivos antigos nesta etapa.

Falha ao abrir/gravar log não vira sucesso silencioso. Exceção fatal na UI é
registrada, exibida em MessageBox e encerra o app com código 1. Erros operacionais
de conexão recuperáveis terão tratamento por serviço quando esses serviços existirem.

## Janela e fullscreen

Título iMirror, status sem dispositivo, três botões, placeholder de vídeo e
painel de logs. Layout simples; tema e métricas da fase 8 ficam pendentes.
Fullscreen alterna moldura/resize/estado; preserva estado normal ou maximizado
anterior. F11 alterna e ESC sai apenas dentro da janela. Sem Topmost ou hotkey
global. Manifesto asInvoker e PerMonitorV2; não exige administrador.

## Fronteira futura de vídeo

```text
iPhone -- AirPlay/LAN --> UxPlay.exe (processo externo GPL)
                            ↑ iniciar/parar/logs
                         iMirror.AirPlay
                            ↑ estado
                         iMirror.App

Somente após vídeo aprovado:
iMirror.Input --> iMirror.Bluetooth -- HOGP --> iPhone/AssistiveTouch
```

Prova inicial usará janela própria do UxPlay. Integração visual posterior dentro
da fase 2 compara RTP loopback + renderer GStreamer e HWND, conforme pesquisa.
Manter comunicação e obrigações de licença documentadas; não linkar UxPlay nos
assemblies. Nunca considerar processo iniciado como prova de iPhone conectado
ou vídeo renderizado.

## Validação e avanço

`tools/test.ps1` recompila, verifica funções atuais e salva PNG renderizado da
janela. Sem pacotes de testes NuGet: o harness retorna 0/1 e roda testes WPF/Win32
reais. `dotnet test` não é o comando deste harness. Na introdução de lógica mais
complexa, avaliar framework de testes convencional.

Os testes desta fase não comprovam rede, rádio, iOS ou protocolo. A fase 2 tem
aceitação própria em `docs/phase2-plan.md`, sem autorizar fases posteriores.
