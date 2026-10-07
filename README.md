# iMirror

![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

Espelhe e controle o iPhone pelo Windows: **AirPlay/UxPlay** recebe o vídeo em uma janela externa do **GStreamer**; **Bluetooth LE HID** envia mouse e teclado. Interface WPF escura, com páginas de Espelhamento, Controle, Teclado e Configurações. Sem jailbreak.

## Estado físico

Validação informada pelo usuário: iPhone 14 em iOS 27.0.1, Windows 10 x64 build 19045 e adaptador Realtek com BLE peripheral.

- AirPlay: conexão e vídeo externo confirmados.
- BLE/input: fisicamente validados no checkpoint **8262f344**. A conexão posterior apresentou quedas; o transporte atual voltou ao schema daquele checkpoint.
- **READY FOR STABLE IPHONE HID REVALIDATION** — conexão contínua de 10 minutos e reconexão no mesmo provider ainda precisam do iPhone.

A revalidação atual segue uma única rotina em [iOS Stable](docs/IOS_STABLE_HID_REVALIDATION.md). Wheel, rotação e demais testes de input permanecem no [roteiro físico](docs/PHASE3B_VALIDATION.md). Testes sintéticos não substituem essa validação.

## Instalação e release

Abra **dist/iMirror/iMirror.exe**. Não precisa de PowerShell, Visual Studio ou SDK para usar.

| Pacote | .NET |
|---|---|
| dist/iMirror | Framework-dependent; exige .NET Desktop Runtime 10 x64. Neste checkout reutiliza também o .NET local. |
| dist/iMirror-Portable.zip | Self-contained; extraia a pasta inteira, com .NET incluído. |

Ambos preservam UxPlay **1.73.7**, GStreamer **1.28.7**, HEVC **-h265**, **d3d11videosink**, nome **iMirror - Windows** e portas **35000–35002**. O pacote não contém instalação completa do MSYS2, SDK, compilador ou cache. Veja [requisitos do release](docs/RELEASE.md) e [tamanhos medidos](docs/disk-usage-after.md).

~~~text
dist/
  iMirror/
    iMirror.exe
    airplay.json
    send-ctrl-c.ps1          # helper interno de encerramento
    LEIA-ME.md
    runtime/airplay/         # DLLs/plugins verificados, manifesto e licenças
  iMirror-Portable.zip       # alternativa com runtime .NET
~~~

Requisitos: Windows x64 build 19041+, [compatível com .NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/windows), Bluetooth LE com **Peripheral Role/GATT**, Bonjour instalado e rodando, PC e iPhone na mesma rede. Só BLE central não basta.

O Bonjour é um serviço instalado separadamente; a DLL do pacote não substitui esse serviço. Ao mover o pacote para outro PC ou caminho, a regra do firewall precisa autorizar o novo caminho de uxplay.exe na rede privada. Não desative o firewall. Esta refatoração não altera Bonjour, rede ou firewall.

## Uso

1. Em **Espelhamento**, clique **Iniciar AirPlay**. No iPhone, abra Central de Controle → Espelhamento de Tela → **iMirror - Windows**.
2. O vídeo abre em janela externa. **Abrir janela de vídeo** traz essa janela para frente quando o Windows permitir.
3. Em **Controle**, clique **Conectar Bluetooth**. No iPhone: Ajustes → Acessibilidade → Toque → **AssistiveTouch → Dispositivos → Dispositivos Bluetooth**, selecione o PC uma vez.
4. Com vídeo ativo e subscribers live de **mouse e teclado**, clique **Ativar controle** e use a janela de vídeo em foco.
5. **Esc / Ctrl+Alt+Q** interrompe a captura e libera o cursor. **F11** alterna fullscreen da interface; Esc sai. A janela externa continua independente.

**Mouse:** velocidade linear de 0,25x–3x, padrão 1x; scroll usa intensidade de 1–5. O controle é relativo, preserva aspect ratio/letterboxing e DPI. Cursor local oculto só dentro do vídeo; saída, perda de foco, desconexão e fechamento enviam releases e devolvem o input ao Windows.

**Teclado / ABNT2:** escolha Auto, Português Brasil ABNT2 ou US. Configure o mesmo layout em Ajustes → Geral → Teclado → Teclado Físico no iPhone. Usa teclas físicas e composição por teclas mortas. O perfil padrão **iOS Stable / Known Good** aceita usages até 0x65: International1 do ABNT2 (`/` e `?`, usage 0x87) fica explicitamente indisponível; o layout US mantém esses caracteres. O mapper não estende o descriptor. Reative o controle após mudar layout ou velocidade. Para usar também o teclado do iPhone, ative Mostrar Teclado na Tela no AssistiveTouch.

**Reconexão:** use o mesmo caminho do AssistiveTouch se necessário e reative o controle. A captura não recomeça automaticamente após perda do link/suspensão.

**Pareamento HID:** após o primeiro clique, **Cancelar espera** apenas recolhe a UI; **Mostrar pareamento** volta a exibi-la. Provider, advertising e vínculo continuam ativos. Queda, timeout, parar AirPlay/controle ou perder foco não recriam HOGP. Controle mostra geração, lifetime, etapas GATT/Info/Map e subscribers reais; ambos live habilitam input. Advertising usa recuperação limitada de 1/2/5 s no mesmo provider. Em Configurações → Recuperação avançada, **Parar serviço HID** pausa o anúncio com confirmação, preservando o objeto; **Reiniciar serviço HID** recria explicitamente, somente após parada confirmada. **Esquecer vínculo deste iPhone no Windows** exige host da sessão HID exata, bond conhecido e confirmação; mostra o resultado real de UnpairAsync. Nenhuma busca/remoção por nome.

**Janelas / modo foco:** o painel abre dimensionado e centralizado na WorkArea, com último tamanho/posição validado. A janela **iMirror — iPhone** usa o ícone vermelho e ajuste opcional à resolução/orientação/DPI; não interfere no resize manual até mudar o stream. Em Configurações, ative **Ocultar painel durante o espelhamento**. Ele só desaparece com renderer visível; volta pela bandeja ou **Ctrl+Alt+I**, com **Ctrl+Alt+Shift+I** como fallback. Durante captura, o atalho também libera input. Parada, erro ou fechamento do renderer recuperam o painel. F11 continua independente.

**Gravação:** ainda não implementada no código atual; a página informa sua indisponibilidade.

**Diagnóstico:** botão no topo ou Configurações. Painel limitado com filtros, Copiar, Limpar e Abrir pasta. Limpar afeta só a lista; arquivos continuam persistidos. Logs de runtime ficam em %LOCALAPPDATA%/iMirror/logs, até 5 MiB por arquivo e 5 arquivos por família. No desenvolvimento, logs de controle/tentativas também usam logs/ do projeto.

O filtro normal preserva eventos relevantes; **Verbose** mostra detalhes nativos recentes sem apagar a lista relevante. Arquivos continuam completos e rotativos; Bluetooth inclui session/id local e geração do provider, sem MAC, PIN ou payload de teclas. Ícones são assets embutidos locais; nenhum Imgur é acessado em runtime. Veja [validação do polimento](docs/FINAL_POLISH_VALIDATION.md).

## Desenvolvimento e build

**Compatibilidade HID estável:** Report Map de 113 bytes idêntico ao commit 8262f344, com SHA256 e banco GATT congelados por testes e guard de build. Veja [comparação, validação e teste físico único](docs/IOS_STABLE_HID_REVALIDATION.md). As observações anteriores estão no [histórico](docs/BLUETOOTH_CONNECTION_INVESTIGATION.md).

Após 10 s sem etapas HID, a UI informa que a conexão não chegou ao HOGP; o provider continua ativo. Logs incluem saúde HID periódica. O coletor nativo opcional existente também classifica 0x13 como `REMOTE_TERMINATION`, com elapsed LE quando observado e snapshot sanitizado das etapas; a correlação entre link do rádio e host GATT permanece explícita. Nenhuma nova coleta é necessária para iniciar a revalidação estável.

SDK .NET **10.0.401** conforme global.json; Git; Windows. Restore só usa pacotes Microsoft da fonte oficial configurada. O SDK local é mantido em .tools/dotnet. O runtime nativo local foi reduzido; **recompilar WPF não exige MSYS2 completo**.

~~~powershell
.\tools\build-release.ps1
~~~

Esse comando limpa artefatos locais, compila Release, executa as três suites, probe BLE self-test, fixtures de rede e teste nativo do cursor, publica as duas variantes e cria o ZIP. Nenhum componente global é instalado. Feche o iMirror antes de reconstruir o release.

~~~powershell
.\tools\test.ps1 -Configuration Debug
.\tools\test.ps1 -Configuration Release
.\tools\clean-project.ps1
~~~

Clean preserva fontes, .git, SDK, runtime, assets, dist, configurações e evidência física; caches globais não são tocados. Temporários locais em uso podem ser preservados.

O runtime enxuto é montado por tools/native-runtime.ps1: closure dos imports PE normais/delay, plugins identificados com gst-inspect e testes nativos. Dois plugins de encode ficam somente no runtime de desenvolvimento para testes H.264/H.265; o release usa a cadeia de recepção. O rollback local foi preservado em .tools/rollback/; com AirPlay parado, tools/restore-native-runtime.ps1 restaura DLLs/plugins anteriores no mesmo caminho.

Para recompilar o UxPlay, será necessário preparar novamente um toolchain MSYS2 UCRT64 completo conforme [dependências nativas](docs/NATIVE_DEPENDENCIES.md) e [fonte/patch do binário](third_party/README.md). O runtime reduzido não inclui pacman, headers ou compiladores. Binários, downloads, caches e logs locais não são versionados.

## Limitações e referências

Vídeo externo; áudio desativado; gravação indisponível; ponteiro absoluto/digitizer desabilitado. Não implementa o protocolo privado de iPhone Mirroring da Apple. Outros adaptadores, Windows 11 e outros iPhones precisam de validação própria.

[UxPlay](https://github.com/FDH2/UxPlay/tree/v1.73.7) · [GStreamer](https://gstreamer.freedesktop.org/) · [AssistiveTouch](https://support.apple.com/111775) · [BLE HID e limites](docs/PHASE3_CONTROL.md) · [licenças e build nativo](third_party/README.md)
