# Fase 2 — até o gate do iPhone

Fases 0/1 aprovadas. Escopo atual somente AirPlay; arquitetura Bluetooth/Input
inalterada. Vídeo real na janela externa é obrigatório antes da investigação interna.

## Referência fixa e pesquisa inicial

UxPlay **v1.73.7**, commit `df67c212a433cf6dda3676dd40c097900d24e645`.
Sem dependência 1.74. A [release oficial](https://github.com/FDH2/UxPlay/releases/tag/v1.73.7)
documenta Windows 10/11 x64 e fix do TEARDOWN alterado no iOS27. Não substitui teste
do modelo/iOS real. [README da tag](https://github.com/FDH2/UxPlay/blob/v1.73.7/README.md)
e fontes uxplay.cpp/httpd.c/raop_handlers.h/video_renderer.c foram conferidos.

| Item | Escolha / constatação |
| --- | --- |
| Windows/MSYS2 | UCRT64 MinGW, NO_MARCH_NATIVE=ON; DLLs/plugins/scanner preservados. |
| Bonjour | DLL x64 + Bonjour Service; sem beacon BLE/mDNS experimental. |
| Nome | -n "iMirror - Windows" -nh, sem sufixo hostname. |
| Latência | -vsync no elimina espera por sincronização A/V; resultado medido ainda N/A. |
| Áudio | -as 0 suprime reprodução; alternativa upstream -a. |
| Vídeo | avdec_h264 software + d3d11videosink; baseline H264. |
| Rotação | README registra histórico D3D12 e workaround D3D11. Escolha conservadora, pendente teste físico. |
| Reconexão | Manter receiver após reset; reiniciar após término/erro. Testes de estado não validam protocolo/rede. |
| -vrtp | rtph264pay/rtph265pay envia vídeo descriptografado; desativado no milestone externo. |
| -artp | L16/S16BE, 44100 Hz estéreo; fora do pipeline atual sem áudio. |
| iOS atual | Upstream 1.73.7 inclui fix iOS27; compatibilidade deste iPhone pendente. |

`-d 1` fornece eventos sem dump de payload. Tag Windows bufferiza debug redirecionado;
patch externo mínimo torna setbuf(stdout,NULL) incondicional. Fonte/licença/patch
separados dos assemblies C#.

## Implementação e sequência restante

1. Implementado checker PE x64, versões, executáveis/plugins e Bonjour Service:
   Ready/UxPlayMissing/GStreamerMissing/ServiceDiscoveryMissing/InvalidInstallation.
2. Implementado lifecycle serializado e lock entre apps no mesmo diretório de sessão,
   argumentos centrais sem shell, stdout/stderr, timeout/cancelamento, exit code,
   parada graciosa e kill restrito à própria árvore quando necessário.
3. Implementados estados por logs, UI assíncrona, fechamento com limpeza e métricas
   reais quando disponíveis. Timeout nunca é promovido a conexão bem-sucedida.
4. Preparados localmente MSYS2/GStreamer e build UxPlay. Scripts idempotentes de
   preparação/doctor e cinco ciclos reais via WPF prontos. Instalação silenciosa
   Bonjour bloqueada por privilégios (1603); serviço/firewall exigem o script
   administrativo. Roteiro em READY_FOR_IPHONE.md. Fase 2 segue
   **PENDING PHYSICAL IPHONE VALIDATION**, mesmo após READY FOR IPHONE TEST.
5. **Gate pendente:** iPhone encontrar receiver, conectar e mostrar vídeo externo.
6. Pendentes: rotação, três reconexões, lock/unlock, perda de rede, stop/restart e limpeza.
7. Só após 5/6 investigar/prototipar frames internos via -vrtp. Limite registrado em
   video-integration.md. Não iniciar Bluetooth.
8. Medir FPS/latência com metodologia explícita quando houver stream observável.

## Evidência de estado

| Saída upstream | Significado |
| --- | --- |
| register_dnssd: advertised AirPlay service | Aguardando, registro local; não prova descoberta física. |
| connection request from ... with deviceID | Conectando, cliente identificado. |
| Mirroring initialized successfully | Conectado por protocolo. |
| Begin streaming to GStreamer video pipeline | Vídeo encaminhado; visualização precisa de confirmação. |
| begin video stream wxh = ...; source ... | Dimensões/rotação do stream. |
| video_reset: type = RTP_Shutdown / NoHold | Reset/desconectado; aguardar nova solicitação. |
| lost connection with client | Perda de conexão identificada. |
| Error initialising socket 10048 | Porta ocupada. |
| DNSServiceRegister / No DNS-SD Server found | Erro de descoberta. |
| GStreamer error (video) | Erro do renderer. |
| Processo termina | Erro/exit code, salvo parada solicitada. |

Sockets aceitos/fechados e contagens auxiliares não são prova de mirroring. Mensagens
desconhecidas permanecem no log; pode haver atraso até o UxPlay reportar perda física.
