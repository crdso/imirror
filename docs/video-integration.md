# Integração de vídeo — gate fechado

O usuário determinou primeiro validar o iPhone na janela externa, **somente depois**
investigar/implementar vídeo no WPF. Milestone pendente: comparação arquitetural,
protótipo e integração interna **não executados**, sem arquitetura final ou benchmarks.

O [README v1.73.7](https://github.com/FDH2/UxPlay/blob/v1.73.7/README.md) confirma -vrtp
para H264/H265 descriptografado com rtph264pay/rtph265pay. A cauda pode conter
`config-interval=1 ! udpsink host=127.0.0.1 port=5000`. Argumento não ativo nesta entrega.
Atualmente UxPlay renderiza externamente com avdec_h264 + d3d11videosink.

Fluxo candidato solicitado, a validar após gate:

```text
iPhone → UxPlay externo → -vrtp / loopback → depayloader/decoder
       → frames reais → renderer WPF
```

| Alternativa | Questões reservadas para protótipo/comparação |
| --- | --- |
| A. appsink + interop | ABI, ownership GstSample/Buffer, stride/formato, filas limitadas, descarte de frames antigos, cópia para WriteableBitmap/superfície GPU. |
| B. RTP/decoder + memória compartilhada | Confirmar transporte shm no Windows; avaliar helper e MemoryMappedFile caso necessário, sincronização e buffers variáveis. Não presumir shmsink Linux pronto no Windows. |
| C. Media Foundation | Depayload RTP, SPS/PPS, timestamps, COM, decoder/HEVC disponível no Windows alvo e interop de superfícies. |
| D. GPU/helper próprio | D3D11/D3DImage, textura compartilhada, recriação na rotação, limites WPF, separação do processo GPL. |

Para todas: comparar latência fim a fim, estabilidade na rotação/reconexão, complexidade,
cópias CPU/GPU, H264/H265 e screenshot/gravação de frames/stream reais. Resultados atuais:
**N/A**. Não há alegação de alternativa testada ou mais rápida.

Referências da próxima etapa: [appsink](https://gstreamer.freedesktop.org/documentation/app/appsink.html),
[shmsink](https://gstreamer.freedesktop.org/documentation/shm/shmsink.html),
[Media Foundation](https://learn.microsoft.com/en-us/windows/win32/medfound/media-foundation-programming-guide).

Protótipo deverá demonstrar renegociação de caps/dimensões em retrato/paisagem,
descarte de frames atrasados e limpeza sem travar UI. Não usar captura Windows,
screenshots repetidos, OCR ou automação visual. SetParent/reparenting não estão
implementados nem serão arquitetura final. Atualizar comparação/evidências após teste
físico, mantendo a fase 2 aberta e sem iniciar Bluetooth.
