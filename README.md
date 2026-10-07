# iMirror — AirPlay externo + controle BLE HID

Aplicativo Windows C#/WPF. **Iniciar AirPlay** verifica dependências e supervisiona
UxPlay como processo externo. O vídeo será exibido na janela GStreamer, sem áudio.
O usuário confirmou conexão e vídeo AirPlay na janela externa. Bluetooth agora oferece HOGP composto, UI e captura restrita ao vídeo.

**PHASE 3: IMPLEMENTED — PENDING PHYSICAL VALIDATION.** [Uso, arquitetura, arquivos e limites](docs/PHASE3_CONTROL.md).

**Registro histórico do teste inicial:** receiver aparece no iPhone com iOS 27.0.1,
mas duas conexões falharam. Próximo teste: perfil temporário UxPlay-iOS27,
**1.73.7 preservado + -h265**, logging sanitizado; veja
[investigação iOS 27](docs/IOS27_AIRPLAY_TEST.md). Bonjour já foi instalado pelo
usuário e está Running/Automatic. As notas iniciais de instalação abaixo são
históricas; não reinstale nem modifique firewall para investigar essa negociação.

## Nesta máquina

| Componente | Preparação / versão |
| --- | --- |
| .NET | SDK local 10.0.401; Desktop Runtime 10.0.12 |
| UxPlay | 1.73.7, commit df67c212a433cf6dda3676dd40c097900d24e645 |
| Build UxPlay | MSYS2 UCRT64 x64; patch externo de stdout imediato, documentado em third_party |
| GStreamer | 1.28.7; base/good/bad/libav; avdec_h264 + d3d11videosink |
| Bonjour | 3.1.0.1; serviço instalado e validado Running |
| Sistema validado automaticamente | Windows 10 x64 build 19045; Windows 11 ainda requer validação própria |

As ferramentas portáteis estão em `.tools`; não houve instalação global de MSYS2/GStreamer,
alteração permanente de PATH ou aplicação de firewall. Exige Windows x64 e .NET Desktop 10.
Consulte a [matriz oficial .NET/Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Preparação automática e primeiro teste

O MSI foi verificado como **Valid**, Apple, versão 3.1.0.1. A tentativa silenciosa
retornou **1603: privilégios insuficientes**; não foi repetida. Serviço e regras
continuam pendentes. O [roteiro de uma página](docs/READY_FOR_IPHONE.md) explica a
única etapa administrativa e o teste quando o iPhone estiver disponível.

Na pasta do projeto, execute `tools\finish-airplay-setup-admin.ps1` uma vez em
PowerShell como administrador. Ele verifica novamente assinatura/hash, instala
Bonjour se ausente, configura Running/Automatic e aplica firewall restrito.
Depois execute `tools\prepare-airplay.ps1` como usuário normal: Debug/Release,
todos os testes, diagnóstico e abertura do app somente se tudo passar.
Não reinstala componentes corretos. `-NoLaunch` permite apenas validar.

`tools\airplay-doctor.ps1` retorna 0 somente para **READY FOR IPHONE TEST**, ou 1
com os itens ausentes. Executa versões/plugins, serviço, socket mDNS, firewall,
evidências atuais de build/testes, renderer sintético e cinco ciclos reais pela
UI WPF, incluindo captura stdout/stderr, exclusão de duplicatas, parada e browse
local. Pare o receiver antes de executar o diagnóstico.

| Regra de entrada | Protocolo / portas | Perfil / origem | Executável |
| --- | --- | --- | --- |
| iMirror-AirPlay-TCP | TCP 35000–35002 | Private / LocalSubnet | `.tools\msys64\ucrt64\bin\uxplay.exe` |
| iMirror-AirPlay-UDP | UDP 35000–35002 | Private / LocalSubnet | `.tools\msys64\ucrt64\bin\uxplay.exe` |
| iMirror-Bonjour-mDNS | UDP 5353 | Private / LocalSubnet | `%ProgramFiles%\Bonjour\mDNSResponder.exe` |

`configure-airplay-firewall.ps1` conserva a prévia padrão e aplicação com `-Apply`;
restringe também regras de entrada mais amplas criadas pelo instalador **para esse
mesmo executável Bonjour**. Não libera perfil Public nem desativa firewall.
Resultados sanitizados em `logs\airplay-readiness.log` e `.json`.

Após preparação aprovada, abra **iMirror.lnk**, clique **Iniciar AirPlay** e espere
**Aguardando iPhone...**. Na mesma LAN privada: iPhone → Central de Controle →
Espelhamento de Tela → **iMirror - Windows**. Vídeo permanece na janela externa.
Reporte discovery, conexão, vídeo, portrait, landscape e reconexão conforme
[registro de validação](docs/phase2-validation.md).

Mesmo `READY FOR IPHONE TEST` mantém **PENDING PHYSICAL IPHONE VALIDATION**.
Discovery real, vídeo AirPlay, rotação e reconexão só serão aprovados com iPhone.
Controle BLE tem validação física própria pendente; veja o documento da Fase 3 acima.

## Compilar e testar

```powershell
.\tools\test.ps1 -Configuration Debug
.\tools\test.ps1 -Configuration Release
```

Scripts preferem `.tools\dotnet\dotnet.exe` ou `dotnet` no PATH. Em outra máquina,
instale [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). WinRT usa o pacote oficial Microsoft.Windows.SDK.NET.Ref, restrito por source mapping.
Executável: `src\iMirror.App\bin\Debug\net10.0-windows\iMirror.exe`. Sem runtime global,
use `run.ps1`, que executa pelo host do SDK local. Ainda não é instalador/self-contained.

## Dependências em outra máquina

**Nesta máquina não repita o build nativo: já está pronto.** Preparação pode ocupar vários GB.

1. Prepare [MSYS2 oficial](https://www.msys2.org/) x64 em `<projeto>\.tools\msys64`.
   Aqui foi usado o pacote portátil `msys2-base-x86_64-20260927.tar.xz` da
   [release oficial](https://github.com/msys2/msys2-installer/releases/tag/2026-09-27).
   SHA256: `EA2F31A0B6ADE63914CE441FFB022F0F6AA96982BFEFA2326460A26D5FB01322`.
   Pode-se usar o instalador interativo oficial escolhendo essa pasta. Complete as
   atualizações indicadas pelo MSYS2. Abra **UCRT64**, não MSYS/MINGW32.
2. No UCRT64, instale explicitamente:

   ```bash
   pacman -S --needed mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-cmake mingw-w64-ucrt-x86_64-ninja mingw-w64-ucrt-x86_64-tools mingw-w64-ucrt-x86_64-libplist mingw-w64-ucrt-x86_64-gstreamer mingw-w64-ucrt-x86_64-gst-plugins-base mingw-w64-ucrt-x86_64-gst-plugins-good mingw-w64-ucrt-x86_64-gst-plugins-bad mingw-w64-ucrt-x86_64-gst-libav
   ```

3. Tenha Git instalado. Em PowerShell na pasta do projeto, execute `tools\build-uxplay.ps1`.
   O script anuncia downloads/build locais, fixa tag/commit UxPlay e cabeçalho Apple,
   valida hash/assinatura do MSI, extrai DLL x64, gera import library e compila.
   **Não instala Bonjour Service nem configura firewall.** MSYS2 usa pacotes rolling:
   GStreamer futuro pode diferir de 1.28.7. Lista local em `.tools\msys2-packages.txt`.
4. Configure Bonjour/firewall usando `finish-airplay-setup-admin.ps1` e valide com
   `prepare-airplay.ps1`. O MSI vem do [CDN oficial Apple](https://swcdn.apple.com/content/downloads/52/06/071-03198/djcqm50b49h4o03eetqwowrdpf4o9sx71z/Bonjour64.msi),
   referenciado pelo [manifesto Microsoft WinGet](https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/Apple/Bonjour/3.1.0.1).
   SHA256: `46E31E284DA64D6C2D366352B8A8ABCF7DB28D3E2A870D8FCF15C4A6FE0A6DD1`.

`airplay.json` centraliza caminhos, nome, porta, decoder e sink. Caminhos relativos
partem do arquivo de configuração. Caminhos nulos permitem busca no PATH. Checker exige
PE x64, UxPlay **exatamente 1.73.7**, ferramentas/plugins e Bonjour em execução.
Não misture x86/x64 ou DLLs MSVC/MinGW; mantenha o diretório `ucrt64` completo.

`IMIRROR_AIRPLAY_CONFIG` seleciona outro arquivo. `run.ps1` seleciona o arquivo deste
projeto. Sem variável, app procura `airplay.json` acima do executável; sem arquivo,
usa defaults/PATH.

## Estados, métricas e diagnóstico

- **Aguardando** vem do registro DNS-SD local: não comprova descoberta no iPhone.
- **Conectando** vem da solicitação identificada do cliente; conexão TCP isolada é ignorada.
- **Conectado** vem da inicialização do mirroring; **stream iniciado**, do envio ao pipeline.
  Logs de streaming não comprovam frames visíveis: confirme a janela externa.
- Reset/TEARDOWN e perda de conexão identificada indicam desconexão. Sockets RTSP
  auxiliares fechando não são prova de desconexão. Sem log pode haver atraso de detecção.
- Receiver: início do processo até anúncio. Conexão → stream: solicitação até primeiro
  streaming. Resolução: dimensões do renderer, não necessariamente resolução física.
  FPS/latência: **N/A**, sem dados medidos.
- Parada tenta CTRL_C no console isolado. Se houver console compartilhado, helper
  recusa sinalizar terceiros. Após 5 s, encerra apenas a árvore do processo iniciado
  pelo iMirror. Nunca mata receivers por nome. Fechamento cancela início e aguarda limpeza.

| Sintoma | Ação |
| --- | --- |
| UxPlayMissing | Conferir UxPlayPath / build nativo 1.73.7 x64. |
| GStreamerMissing | Conferir bin/DLLs; gst-inspect dos elementos appsrc, h264parse, videoconvert, rtph264depay, decodebin, avdec_h264, d3d11videosink. |
| ServiceDiscoveryMissing | sc.exe query Bonjour Service; instalar/iniciar serviço. DLL isolada não substitui serviço. |
| InvalidInstallation | Versão/arquitetura/loader/caminho inválido; detalhes nos logs. |
| Receiver não aparece | Aguardando, mesma LAN, Bonjour, UDP5353, rede privada, firewall, roteador/VPN. |
| Aparece mas não conecta | Conferir TCP/UDP35000–35002 e erros de autenticação/processo. |
| Porta ocupada / 10048 | Fechar outro receiver manualmente ou mudar BasePort e regras. |
| Stream ativo sem imagem | Erros GStreamer, plugin/driver; status não substitui prova visual. |
| Processo encerrou | Exit code e stderr nos logs; iniciar novamente. |
| Build não copia DLL | Fechar iMirror antes de recompilar. |

Logs em `%LOCALAPPDATA%\iMirror\logs`; painel mantém 500 entradas. Use
`IMIRROR_LOG_DIRECTORY` para outra pasta gravável. Nome/ID/IP de dispositivo podem
aparecer no debug; revise antes de compartilhar. F11/ESC controla fullscreen de iMirror;
janela de vídeo externa é independente.

## Documentação

- [Plano](docs/phase2-plan.md), [validação](docs/phase2-validation.md),
  [gate interno](docs/video-integration.md), [inventário fase 2](docs/phase2-files.md).
- [Release UxPlay](https://github.com/FDH2/UxPlay/releases/tag/v1.73.7),
  [README da tag](https://github.com/FDH2/UxPlay/blob/v1.73.7/README.md),
  [dependências/fonte/patch](third_party/README.md).
- [Pesquisa inicial](docs/research.md), [arquitetura base](docs/architecture.md),
  [validação fase 1](docs/validation.md).

Nenhum código UxPlay GPL foi incorporado no C#. Não há captura de tela, OCR,
screenshots repetidos ou reparenting no pipeline.
