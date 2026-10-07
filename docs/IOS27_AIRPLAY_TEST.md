# Teste de negociação com iOS 27.0.1

**PHASE 2: PENDING PHYSICAL IPHONE VALIDATION. Bluetooth/Fase 3 não iniciada.**

O usuário informou que o receiver aparece e duas conexões falharam no iPhone.
Discovery físico foi observado pelo usuário; vídeo, orientação e reconexão ainda
não foram aprovados. A causa da falha de negociação ainda não está identificada.

## Pesquisa primária

- [Issue 535](https://github.com/FDH2/UxPlay/issues/535): relato de iOS 27 beta com
  SETUP/feedback e inicialização, mas sem conexão de dados do mirror; inclui
  `combinedGetInfoWithControlSetup`. A presença dessa chave não prova a causa.
- [Relato Windows 11 / UCRT64](https://github.com/FDH2/UxPlay/issues/535#issuecomment-5152130507):
  iOS 27 beta, AirPlay/980.63.2, UxPlay 1.74; `-h265` permitiu vídeo HEVC depois
  de falha de pacote sem payload. Não é confirmação para iOS 27.0.1 nem para
  este notebook.
- [README fixado 1.73.7](https://github.com/FDH2/UxPlay/blob/v1.73.7/README.md):
  essa versão já tem `-h265`, habilita ScreenMultiCodec e cria pipelines H.264 e
  H.265. [Release 1.73.7](https://github.com/FDH2/UxPlay/releases/tag/v1.73.7)
  inclui correção do TEARDOWN do iOS 27; isso não garante negociação bem-sucedida.

## Perfil do próximo teste

`profiles/UxPlay-iOS27.json` usa **o mesmo UxPlay 1.73.7**, nome, portas 35000–35002,
renderer d3d11videosink e decoder base avdec_h264. Adiciona apenas `-h265` e debug
completo `-d`. O upstream converte os elementos h264 para h265 no segundo pipeline;
portanto o decoder HEVC efetivo é **avdec_h265**, sem mudar o decoder H.264.
`-h265` também muda a resolução padrão anunciada pelo upstream para 3840×2160.

Forma do comando (o arquivo rc é gerado no diretório de sessão do iMirror):

```text
uxplay.exe -rc "<SessionDirectory>\uxplay-empty.conf" -n "iMirror - Windows" -nh -p 35000 -vsync no -as 0 -vs d3d11videosink -vd avdec_h264 -h265 -d
```

O log registra **Executable e Arguments JSON exatos**, antes de iniciar o processo.
`airplay.json` e o executável 1.73.7 foram preservados. SHA256 do executável:
`BBEDA5A9757178BC99186F7CA58C80A9ABD47A7EA2DED8043EEAA532B75FD33E`.
1.74 não foi preparado: primeiro será testado o suporte HEVC já presente em
1.73.7. O êxito físico desse fluxo ainda precisa ser verificado; uma eventual
experiência 1.74 deve permanecer separada, conforme solicitado.

## Verificações locais

`gst-inspect` confirmou avdec_h265, d3d11h265dec, decodebin, h265parse,
rtph265depay e x265enc. Fonte sintética → x265enc → h265parse → decoder →
videoconvert → d3d11videosink passou separadamente com avdec_h265, decodebin e
d3d11h265dec: exit0, EOS, janela aberta, 20 quadros, zero perdas em cada caso.
Isso não valida frames recebidos do iPhone.

Debug e Release finais: 0 erros e 0 warnings, 13/13 grupos por configuração.
O perfil HEVC passou cinco ciclos reais de iniciar/aguardar/parar pelo comando
WPF, incluindo anúncio local, dois pipelines inicializados, prevenção de
duplicatas, captura de stdout/stderr e ausência de processos órfãos. As paradas
usaram o fallback restrito ao processo próprio (exit -1), sem saída inesperada.
Os testes locais foram encerrados antes de abrir o iMirror com o perfil temporário.

Testes incluem redaction antes dos logs geral/específico, campos XML divididos
em linhas, respostas, protocolo, SETUP incompleto, saída inesperada e inspeção
TCP vinculada ao PID real (IPv4/IPv6). Um accept iniciado não confirma conexão;
ESTABLISHED é registrado somente quando observado na tabela TCP do Windows.
Se a sessão acabar sem evidência de dados, o log diz **não observado**, sem
atribuir automaticamente a causa à rede ou a combinedGetInfoWithControlSetup.

Bonjour está Running/Automatic. Não houve alteração do firewall. O diagnóstico
anterior aprovado foi preservado em `.cache/readiness/pre-ios27-airplay-readiness.json`.
Nesta sessão o Windows negou leitura das regras; o novo doctor distingue acesso
indisponível de regra ausente. Essa limitação não é evidência de falha de discovery.

## Log e próximo teste físico

Abra iMirror.lnk e clique Iniciar AirPlay. O perfil temporário é selecionado pelo
launcher; airplay.json continua como fallback. No iPhone tente espelhar novamente
para iMirror - Windows e informe o resultado e horário.

Log: **logs/iphone-airplay-attempt.log**, append e flush imediato. Registra métodos,
osVersion/sourceVersion/User-Agent AirPlay, combinedGetInfoWithControlSetup,
dataPort, RECORD/feedback/TEARDOWN, erros categorizados, estados e saída inesperada.
Chaves, provas, PIN, Authorization, nomes/IDs de dispositivos, endereços e
payloads ficam fora dos logs. Linhas não autorizadas são descartadas em memória;
o total omitido aparece no fim. Os testes automatizados usam arquivo separado
em `.cache`, nunca o arquivo da tentativa física.

Para voltar: feche iMirror e execute
`tools/select-airplay-profile.ps1 -Profile Stable -Launch`.
