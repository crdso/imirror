# Mouse capturado — 2026-10-08

O usuário confirmou que o BLE HID voltou a funcionar. O problema de mouse estava na captura local: deltas de posições do cursor do Windows eram convertidos para coordenadas do stream por `ToStream`, ampliando o movimento quando o vídeo era menor que a resolução do iPhone. Ao sair do viewport, o cursor do Windows era liberado e o baseline reiniciado. A posição do ponteiro relativo no iPhone nunca foi a mesma posição do cursor local.

Agora **Ativar controle** oculta o cursor do Windows e o confina a um ponto interno do vídeo. Somente a bolinha do iPhone representa onde o clique ocorrerá. Raw Input fornece deltas físicos com a velocidade configurada (padrão 1x), sem multiplicação por tamanho da janela, resolução do stream ou DPI. Não envia eventos de movimento baseados em entrada/reentrada/recentralização do cursor do Windows. Não promete posicionamento absoluto nem deduz coordenadas do ponteiro remoto.

O viewport continua calculado com aspect ratio/letterboxing e coordenadas físicas. Resize/rotação reposicionam o ponto local de captura sem gerar movimento HID. Click, wheel, releases, fila limitada e teclado ficam no caminho existente. O Report Map e todo o transporte iOS Stable permanecem iguais; nenhuma mudança AirPlay/GStreamer/renderer/firewall/Bonjour.

**Esc / Ctrl+Alt+Q** devolvem imediatamente o mouse ao Windows. Perda de foco, conexão, erro ou fechamento também liberam confinamento. Não aguarda a conclusão BLE para devolver o cursor. O registro Raw Input só vive durante a captura; cleanup remove o registro e a janela do cursor. Nenhum ShowCursor global, driver ou dispositivo extra instalado.

APIs oficiais: [Raw Input / RAWMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse), [ClipCursor e obrigação de liberar antes de ceder controle](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor).

## Teste no iPhone

Abra a versão nova, conecte Bluetooth e AirPlay usando o vínculo existente; não precisa esquecer/parear novamente. Aguarde mouse e teclado live, clique **Ativar controle** e use apenas a bolinha do iPhone. Mova até as bordas da tela do telefone: o cursor do Windows não deve aparecer/sair para o PC. Pressione **Esc** para usar o mouse normalmente no PC. Se necessário, diminua Velocidade para 0,5x e reative o controle; aceleração/DPI do mouse e ajuste de apontador no iPhone ainda influenciam a sensação. O teste físico de movimento desta versão permanece pendente.

## Regressão

Fixtures decodificam RAWMOUSE x86/x64, movimento assinado e flags relativas; rejeitam absoluto, pacote truncado e dispositivos de outro tipo. O teste nativo usa janela GStreamer real com HID fake: valida cursor oculto/confinado, registro Raw Input, tentativa de escapar sem movimento HID, liberação antes de terminar um release BLE deliberadamente bloqueado, cursor livre e remoção do registro no Stop. Testes de foco negativo preservam fail-open. A ativação assistida de foreground existe apenas no harness de teste; a aplicação mantém a autorização pelo clique do usuário.
