# Próximo teste com o iPhone

**PHASE 2: PENDING PHYSICAL IPHONE VALIDATION. Fase 3 não iniciada.**

Seu iPhone com iOS 27.0.1 já encontrou o receiver, mas duas conexões falharam.
O próximo teste usa o perfil temporário **UxPlay-iOS27**, com **UxPlay 1.73.7 +
-h265** e debug sanitizado. O binário, configuração original, portas e firewall
foram preservados. Bonjour está Running/Automatic.

1. Conecte notebook e iPhone à mesma rede confiável, privada no Windows.
2. Abra **iMirror.lnk**, na pasta do projeto.
3. Clique **Iniciar AirPlay** e aguarde **Aguardando iPhone...**.
4. No iPhone: Central de Controle → **Espelhamento de Tela**.
5. Selecione **iMirror - Windows** e tente conectar novamente.
6. Se conectar, confirme vídeo na janela externa GStreamer; depois teste portrait,
   landscape e reconexão. Se falhar, informe a mensagem e o horário.

Reporte: receiver apareceu? conectou? vídeo? portrait? landscape? reconexão?

O log da próxima tentativa fica em **logs\iphone-airplay-attempt.log**, com
métodos de negociação, versões do cliente, dataPort, TCP do mirror e erros.
Chaves, PIN, provas criptográficas, identificadores e payloads não são gravados.
O caminho aparece na interface. As tentativas anteriores não foram reconstruídas.

Para voltar ao perfil original, feche iMirror e execute:

```powershell
.\tools\select-airplay-profile.ps1 -Profile Stable -Launch
```

Os testes HEVC locais passaram; isso ainda não comprova vídeo AirPlay do iPhone.
Detalhes: [IOS27_AIRPLAY_TEST.md](IOS27_AIRPLAY_TEST.md).