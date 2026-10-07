# Correção da janela externa de vídeo

## Causa reproduzida

`ReceiverProcessFactory` iniciava UxPlay com `CreateNoWindow=false` e `WindowStyle=Hidden`. No .NET/Windows, `Hidden` define `STARTF_USESHOWWINDOW` e `SW_HIDE`. Isso interfere na primeira chamada `ShowWindow` do GStreamer: a janela existe, mas permanece invisível. `-nh` apenas retira o hostname do nome anunciado e foi preservado.

A comparação local, antes de editar o produto, executou a mesma fonte sintética com três sinks:

| Sink | Fábrica antiga (Hidden) | Normal | Normal + sem console |
|---|---|---|---|
| d3d11videosink | janela GSTD3D11 invisível | visível | visível |
| d3d12videosink | janela GstD3D12Hwnd invisível | visível | visível |
| autovideosink → d3d12videosink | invisível | visível | visível |

Todas as nove execuções terminaram com exit code 0. No caso D3D11 oculto, o sink contabilizou 90 frames renderizados e zero descartados: renderizar/receber pacotes não comprovava uma janela visível. Evidência: `.cache/renderer-investigation/before/comparison.json` e logs sintéticos na mesma pasta.

## Correção

O processo agora usa `WindowStyle=Normal` e `CreateNoWindow=true`. A segunda opção evita somente o console; mantém a janela GUI do GStreamer, os argumentos separados, stdout/stderr redirecionados e o encerramento restrito ao processo do receiver. Mantidos UxPlay 1.73.7, `-h265`, `d3d11videosink`, decoder atual, nome e portas. Nenhuma alteração em Bonjour, WSL, Wi-Fi, firewall ou negociação.

O parser distingue `GStreamer`, `Decoder`, `VideoRenderer`, `SinkWindow` e `NativeRuntime`. Mensagens WARN não mudam o estado para erro; erros genéricos após o anúncio/início do espelhamento não são `StartupFailed`. O perfil de diagnóstico conserva texto nativo de falhas do renderer, criação de elementos, sink efetivo, transições de estado e dimensões, redigindo dados de autenticação/criptografia, IPs e identificadores. Mensagens originais descartadas pelos logs antigos não podem ser recuperadas.

Após pacotes de vídeo, o app inspeciona somente as janelas do próprio receiver e registra classes, quantidade criada e visibilidade. Essa inspeção não move nem mostra janelas e não afirma que frames do iPhone foram decodificados.

## Validação e teste físico

Testes de regressão passam fontes sintéticas por `openh264enc → h264parse → avdec_h264 → d3d11videosink` e `x265enc → h265parse → avdec_h265 → d3d11videosink`, com a fábrica real, verificando janela visível, frames contabilizados, EOS e exit 0. Também foi comparado UxPlay real iniciado diretamente por PowerShell com `UxPlayProcessService`: ambos anunciam, criam pipelines H.264/H.265 e permanecem aguardando. Esses testes locais não substituem a sessão física.

Abra **iMirror.lnk**, clique **Iniciar AirPlay** e selecione **iMirror - Windows** no iPhone. Após Streaming, confira a janela externa. O placeholder do WPF permanece esperado nesta etapa. Teste portrait, landscape e reconexão. Logs: `logs/iphone-airplay-attempt.log` e logs do app indicados na interface, incluindo entradas `Video renderer` e `Video window`.

**PHASE 2: PENDING PHYSICAL IPHONE VALIDATION. Fase 3 não iniciada.**

Arquivos de código/configuração do teste alterados nesta correção:

- `src/iMirror.AirPlay/ReceiverProcess.cs`
- `src/iMirror.AirPlay/UxPlayProcessService.cs`
- `src/iMirror.AirPlay/UxPlayLogParser.cs`
- `src/iMirror.AirPlay/AirPlayStatus.cs`
- `src/iMirror.AirPlay/NegotiationLogFilter.cs`
- `src/iMirror.AirPlay/VideoRendererDiagnostics.cs` (novo)
- `src/iMirror.AirPlay/VideoWindowInspection.cs` (novo)
- `tests/iMirror.Phase2.Tests/Program.cs`
- Esta documentação: `docs/VIDEO_WINDOW_FIX.md` (nova).

Os programas/comparações em `.cache/renderer-investigation/` e os outputs `bin/obj` são evidências/artefatos locais de teste, não configuração do produto. Nenhum perfil JSON ou componente nativo foi substituído.

Referências primárias: [ShowWindow/STARTUPINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow), [.NET Process.Windows](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs), [GStreamer 1.28.7 D3D11](https://github.com/GStreamer/gstreamer/blob/1.28.7/subprojects/gst-plugins-bad/sys/d3d11/gstd3d11window_win32.cpp).
