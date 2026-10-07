# iMirror — uso do release

Abra **iMirror.exe**. Não é necessário PowerShell, Visual Studio ou SDK.

`dist/iMirror` é a distribuição menor, framework-dependent: requer **.NET Desktop Runtime 10 x64**. Neste checkout, o EXE também encontra o .NET local em `.tools/dotnet`. A busca relativa é uma opção oficial do [apphost .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/#configure-net-install-search-behavior).

`iMirror-Portable.zip` contém a alternativa **self-contained**, com o runtime .NET incluído. Extraia a pasta inteira antes de abrir o EXE. Nenhuma das duas variantes inclui SDK, compiladores ou caches; ambas mantêm o vídeo externo do GStreamer.

Requisitos: Windows x64 10/11 build 19041+, adaptador Bluetooth com BLE peripheral/GATT, Bonjour instalado e em execução, iPhone e PC na mesma rede. No primeiro uso em outro computador, autorize o receiver `runtime/airplay/bin/uxplay.exe` na rede privada. Regras antigas com caminho de outro EXE não cobrem automaticamente o pacote portátil; não desative o firewall. Nesta tarefa nenhuma regra foi alterada.

1. Em **Espelhamento**, clique **Iniciar AirPlay**. No iPhone: Central de Controle → Espelhamento de Tela → **iMirror - Windows**.
2. Em **Controle**, conecte Bluetooth; no iPhone, pareie o PC em Ajustes → Bluetooth e ative AssistiveTouch.
3. Ative o controle e use a janela externa de vídeo em foco. **Esc / Ctrl+Alt+Q** libera o cursor; **F11** alterna fullscreen da interface.
4. Em **Teclado**, escolha Auto, Português Brasil ABNT2 ou US e use o mesmo layout em Teclado Físico no iPhone. Após ajustes de velocidade/layout, reative o controle.
5. **Diagnóstico** abre os logs; Limpar afeta apenas a lista. Logs em `%LOCALAPPDATA%/iMirror/logs`, limitados a 5 MiB × 5 arquivos por família.

**Pareamento:** acompanhe as etapas em Controle. Subscriber real confirma mouse/teclado; bond ou conexão no painel do iPhone não bastam. Conectar novamente e parar AirPlay não destroem HOGP. Timeout de 30s é apenas visual. Se houver duas entradas do PC, confirme a correta pelas etapas; sem atividade, esqueça somente a entrada tentada e experimente a outra. AssistiveTouch serve para o ponteiro, não para parear. Reset HID em Configurações exige confirmação e recria uma vez somente após parada confirmada. Se o Windows ainda reportar anúncio ativo, o app pede para fechar e reabrir, sem criar outro provider nesta sessão.

**Modo foco:** Configurações → Ocultar painel durante o espelhamento. Volte pela bandeja ou Ctrl+Alt+I (fallback Ctrl+Alt+Shift+I se ocupado). Durante captura, o atalho libera input e mostra o painel. F11 é independente. A janela do iPhone recebe ícone vermelho, título limpo e ajuste opcional de aspect/DPI com debounce. Última posição/tamanho do painel é validada contra os monitores atuais.

Ícones estão embutidos no EXE; não exigem rede nem arquivos do checkout. Diagnóstico → Verbose mostra detalhes recentes; logs completos permanecem rotativos.

Gravação ainda não foi implementada. O mouse é relativo; posicionamento absoluto não é oferecido. A nova UX ainda requer seu teste.

PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED
PHASE 3 INPUT: PHYSICALLY VALIDATED
PHASE 3 UX: UPDATED — PENDING USER VALIDATION

As versões nativas permanecem UxPlay 1.73.7 e GStreamer 1.28.7, `-h265`, d3d11videosink e portas 35000–35002. Veja `runtime/airplay/runtime-manifest.json` para hashes e DLLs, `licenses` para licenças e `UXPLAY_BUILD.md` para fonte/patch/reprodução do binário externo.
