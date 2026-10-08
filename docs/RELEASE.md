# iMirror — uso do release

Abra **iMirror.exe**. Não é necessário PowerShell, Visual Studio ou SDK.

`dist/iMirror` é a distribuição menor, framework-dependent: requer **.NET Desktop Runtime 10 x64**. Neste checkout, o EXE também encontra o .NET local em `.tools/dotnet`. A busca relativa é uma opção oficial do [apphost .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/#configure-net-install-search-behavior).

`iMirror-Portable.zip` contém a alternativa **self-contained**, com o runtime .NET incluído. Extraia a pasta inteira antes de abrir o EXE. Nenhuma das duas variantes inclui SDK, compiladores ou caches; ambas mantêm o vídeo externo do GStreamer.

Requisitos: Windows x64 10/11 build 19041+, adaptador Bluetooth com BLE peripheral/GATT, Bonjour instalado e em execução, iPhone e PC na mesma rede. No primeiro uso em outro computador, autorize o receiver `runtime/airplay/bin/uxplay.exe` na rede privada. Regras antigas com caminho de outro EXE não cobrem automaticamente o pacote portátil; não desative o firewall. Nesta tarefa nenhuma regra foi alterada.

1. Em **Espelhamento**, clique **Iniciar AirPlay**. No iPhone: Central de Controle → Espelhamento de Tela → **iMirror - Windows**.
2. Em **Controle**, clique **Conectar Bluetooth**; no iPhone: Ajustes → Acessibilidade → Toque → AssistiveTouch → Dispositivos → Dispositivos Bluetooth. Selecione o PC uma vez e aguarde mouse e teclado live.
3. Ative o controle: o cursor do PC fica oculto e confinado ao vídeo. Use a bolinha do iPhone; não há cursor local para alinhar. **Esc / Ctrl+Alt+Q** libera imediatamente o mouse para usar o PC. Velocidade padrão 1x ajustável; **F11** alterna fullscreen da interface.
4. Em **Teclado**, escolha Auto, Português Brasil ABNT2 ou US e use o mesmo layout em Teclado Físico no iPhone. Após ajustes de velocidade/layout, reative o controle.
5. **Diagnóstico** abre os logs; Limpar afeta apenas a lista. Logs em `%LOCALAPPDATA%/iMirror/logs`, limitados a 5 MiB × 5 arquivos por família.

**Pareamento:** perfil padrão iOS Stable / Known Good, schema do checkpoint fisicamente validado 8262f344. **Cancelar espera** recolhe apenas a UI; o provider/anúncio/vínculo continuam ativos. Mostre novamente sem iniciar outro provider. Queda, timeout, foco, parar AirPlay ou controle preservam a geração. A recuperação limitada de advertising usa o mesmo provider (1/2/5 s). Parar/reiniciar serviço HID ficam em recuperação avançada com confirmação. Esquecer vínculo no Windows exige DeviceInformation exato da sessão HID conhecida/pareada; botão desabilitado se desconhecido. Bluetooth normal mostrando conectado não confirma HID.

**Teclado estável:** Auto/PT-BR/US preservados. International1 do ABNT2 (`/` e `?`, usage 0x87) não é enviado pelo perfil estável de máximo 0x65; US e demais teclas/dead keys mantidos. Nenhuma expansão silenciosa do Report Map.

**Revalidação única:** feche o app antigo/probe, faça uma limpeza de vínculo antigo no iPhone, abra este release, clique Conectar Bluetooth e selecione o PC uma vez pelo AssistiveTouch. Não alterne rádio/provider. Quando GATT, HID Information, Report Map, keyboard e mouse estiverem ativos, deixe por 10 minutos. Depois desligue somente o Bluetooth do iPhone por 10 s e ligue: a reconexão deve manter Provider generation 1. Cancelar espera não atrapalha esse teste.

**Modo foco:** Configurações → Ocultar painel durante o espelhamento. Volte pela bandeja ou Ctrl+Alt+I (fallback Ctrl+Alt+Shift+I se ocupado). Durante captura, o atalho libera input e mostra o painel. F11 é independente. A janela do iPhone recebe ícone vermelho, título limpo e ajuste opcional de aspect/DPI com debounce. Última posição/tamanho do painel é validada contra os monitores atuais.

Ícones estão embutidos no EXE; não exigem rede nem arquivos do checkout. Diagnóstico → Verbose mostra detalhes recentes; logs completos permanecem rotativos.

Gravação ainda não foi implementada. O mouse é relativo; posicionamento absoluto não é oferecido. A nova UX ainda requer seu teste.

READY FOR STABLE IPHONE HID REVALIDATION

Validação física histórica: checkpoint 8262f344. Conexão de 10 minutos/reconexão deste release ainda dependem do iPhone.

As versões nativas permanecem UxPlay 1.73.7 e GStreamer 1.28.7, `-h265`, d3d11videosink e portas 35000–35002. Veja `runtime/airplay/runtime-manifest.json` para hashes e DLLs, `licenses` para licenças e `UXPLAY_BUILD.md` para fonte/patch/reprodução do binário externo.
