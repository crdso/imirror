# Teste AirPlay somente no Wi-Fi

**PHASE 2: PENDING PHYSICAL IPHONE VALIDATION. Fase 3 não iniciada.**

1. Dê dois cliques em `tools\test-airplay-wifi-only.cmd` e aceite **uma** solicitação do Windows para executar como administrador.
2. Aguarde **READY FOR IPHONE TEST - WIFI ONLY**. O script fecha a instância anterior do iMirror, salva o estado do adaptador WSL e abre o iMirror com AirPlay iniciado no perfil **UxPlay-iOS27**, UxPlay **1.73.7**, **-h265**, portas **35000–35002** e renderer **d3d11videosink**. Não altera Wi-Fi, Ethernet física ou firewall.
3. Mantenha o terminal aberto. Com o iPhone na mesma rede Wi-Fi, abra Espelhamento de Tela e selecione **iMirror - Windows**. Relate conexão, vídeo, portrait, landscape e reconexão.
4. Para encerrar e restaurar a rede, dê dois cliques em `tools\restore-airplay-network.cmd`. Também pode fechar o iMirror. Só o adaptador WSL original será reativado, e somente se estava habilitado antes do teste.

O terminal restaura em `finally`; um processo administrativo separado também restaura se o terminal morrer, receber o pedido de restauração ou atingir o limite de **2 horas**. Não usa tarefa agendada, não muda inicialização e não reinicia o PC. Se houver falha de restauração, execute o mesmo atalho de restauração; ele solicitará elevação uma vez se o processo administrativo não estiver mais disponível. Rodar os atalhos novamente não cria testes paralelos nem sobrescreve um estado de recuperação pendente. Um desligamento abrupto do Windows exige executar o atalho de restauração ao voltar.

Estado recuperável: `.cache\readiness\airplay-network-state.json` (não apagar durante um teste).

Logs: `logs\airplay-wifi-only-test.log` (interfaces, IPs, DNS-SD, argumentos, restauração) e `logs\iphone-airplay-attempt.log` (negociação sanitizada). O teste só libera READY após browse/resolve e IPv4 exclusivamente na interface Wi-Fi. Uma reinicialização controlada de **Bonjour Service** é permitida apenas se continuar retornando IPv4 fora do Wi-Fi. Descoberta/conexão/vídeo/rotação/reconexão reais continuam dependendo do iPhone.
