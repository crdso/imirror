# iOS Stable / Known Good — revalidação HID

**READY FOR STABLE IPHONE HID REVALIDATION**

O schema atual voltou ao checkpoint fisicamente validado `8262f3444cce4e7027bf32ccf0e079986ec8527f`. AirPlay/vídeo externo continuam funcionais. Os 10 minutos e a reconexão deste release ainda precisam do iPhone.

## Comparação e freeze

`git show 8262f344:src/iMirror.Bluetooth/HidSchema.cs` e `HogpPeripheral.cs` foram comparados ao HEAD anterior `aad2c07`. A diferença de schema era a ampliação do teclado em `275d7e5`: Logical Maximum `0x25,0x65` virou `0x26,0x87,0x00`; Usage Maximum `0x29,0x65` virou `0x29,0x87`. O Report Map cresceu de 113 para 114 bytes. A faixa permitida de reports/mapeamento acompanhou essa ampliação. A divergência é comprovada; isso não atribui a causa do HCI 0x13 ao descriptor.

Default **iOS-stable**, bytes completos iguais ao checkpoint:

~~~text
ReportMap length=113
SHA256=3097B7140EDA569B37EECC11502A31AF653B444FFEDB923E0CC85F3CCB5C0A5D
~~~

IDs keyboard 1/mouse 2, reports 8/6 bytes, HID Information, serviços HID/BAS, boot reports, Protocol Mode, Report References, encryption required e CCCDs do Windows já coincidiam com o checkpoint e permanecem iguais. `Compatibility/ios-stable-*` guarda os bytes e a definição de criação/handlers GATT. O guard de build compara bytes/hash e blocos GATT exatos; fixtures negativas provam rejeição de alterações. A referência GATT também tem hash fixo. Nenhum perfil estendido, alternância Battery/Appearance ou publisher paralelo foi introduzido.

Auto/PT-BR/US permanecem, com usages ≤0x65. International1 ABNT2 (`/` e `?`, usage 0x87) não é enviado; a limitação aparece em Configurações. US, demais teclas PT-BR e composição por dead keys permanecem. Texto PT-BR que exija essa tecla é recusado antes de enviar qualquer parte. Selecionar layout não muda o transporte.

## Lifecycle e recuperação

- Conectar cria uma geração. Cancelar espera/Mostrar pareamento só altera UI: provider, advertising e bond continuam ativos.
- Queda, timeout, perda de subscriber/foco, parar captura/AirPlay preservam o provider. Input é liberado e hooks removidos quando necessário; captura exige reativação manual.
- Advertising usa Idle/Starting/Started/Backoff/Stopping. Exception/Aborted/Stopped liberam pending; falta de Started tem watchdog de 10 s. Retries em 1/2/5 s, máximo três, no mesmo provider, cancelados no shutdown. Após 30 s estáveis, uma futura queda recebe novo orçamento limitado. Nenhuma criação automática ou loop agressivo.
- Controle mostra geração/lifetime, etapas e duração HID completo live. Mouse **e** teclado live habilitam input. GATT/Info identifica a entrada correta; após 10 s sem etapas, a UI informa que a conexão não chegou ao HOGP.
- Em recuperação avançada, Parar serviço HID exige confirmação e pausa advertising preservando o objeto. Reiniciar serviço HID recria explicitamente somente após parada confirmada. Parada não confirmada bloqueia retomada insegura; fechamento encerra o processo.
- Esquecer vínculo exige `DeviceInformation` exato da sessão GATT conhecida, `IsPaired=true` e confirmação. Sem busca/remoção por nome. Mostra `DeviceUnpairingResult` real, mantém provider e fica desabilitado sem host elegível. [API UnpairAsync Microsoft](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationpairing.unpairasync?view=winrt-26100).

## Único teste físico

1. Feche o iMirror antigo e qualquer probe BLE. Faça **uma** limpeza do vínculo antigo no iPhone. Se o app anterior conhece um host HID exato pareado, o botão confirmado de esquecer vínculo no Windows também pode ser usado; sem host conhecido, não remova outro dispositivo por nome.
2. Abra `dist/iMirror/iMirror.exe` atualizado e clique **Conectar Bluetooth** uma vez.
3. No iPhone: Ajustes → Acessibilidade → Toque → **AssistiveTouch → Dispositivos → Dispositivos Bluetooth**. Selecione o PC uma vez. É o [caminho de ponteiro indicado pela Apple](https://support.apple.com/pt-br/111775).
4. Não alterne rádio/provider nem volte ao Bluetooth normal para alternar entradas. Aguarde GATT ativo, HID Information/Report Map lidos e ambos subscribers live.
5. Quando chegarem, deixe conectado **10 minutos**. Confira subscribers live e **Provider generation: 1**. Não mexa em mais nada; Cancelar espera só recolhe UI.
6. Depois desligue Bluetooth **somente no iPhone**, espere **10 segundos** e ligue. Deve reconectar mantendo geração 1. Reative a captura manualmente se quiser controlar o vídeo.

Reporte etapas, queda ou estabilidade nos 10 minutos e reconexão com geração 1. Connected nos Ajustes, bond, link LE ou advertising Started isolados não aprovam HID. Nenhuma coleta adicional obrigatória/segundo HOGP. A alternativa upstream só será considerada se esta revalidação conhecida falhar fisicamente.

## Logs

`logs/bluetooth-control.log`: profile/hash/length, callbacks reais, geração, advertising e saúde periódica; sem teclas/payloads/MACs. `bluetooth-hid-stage.json` guarda apenas timestamp, geração local e booleanos. No coletor nativo opcional existente, 0x13 gera `REMOTE_TERMINATION`, elapsed desde LE ConnectionComplete quando observado; links anteriores à coleta ficam unknown. Snapshot recente/stale e correlação GATT/link não comprovada são explícitos: a captura cobre todo o rádio. Nenhuma causa de adaptador/ação do usuário é inventada; nenhuma nova captura foi iniciada nesta entrega.

UI, branding, cursor, modo foco, janela externa/redimensionamento e empacotamento preservados. UxPlay 1.73.7, GStreamer 1.28.7, H.265, renderer, portas e configs preservados. Nenhuma mudança automática de firewall, Bonjour, WSL, Wi-Fi, driver, bond ou rádio.

## Validação da entrega — 2026-10-07

Código dos pacotes: `76444a63fd68a0fa201ac7e3428507e6069d15a2` — `fix(bluetooth): restore validated iOS HOGP compatibility`.

| Verificação local | Resultado |
| --- | --- |
| Debug / Release | 0 warnings, 0 errors em ambos; rebuild Debug final limpo |
| Suites por configuração | 66/66 grupos: UI 12, AirPlay 10, BLE/input 44 |
| HCI/BIP offline | 40/40 fixtures; nenhuma captura nativa iniciada |
| Freeze de schema | 3/3 grupos: baseline aceito, mapa/banco alterados rejeitados |
| Remote termination | 4/4 grupos: elapsed conhecido/unknown, snapshot stale, privacidade e reason preservado |
| Scripts acima em Windows PowerShell 5 | PASS |
| Probe isolado | 11/11 self-tests, sem acesso Bluetooth |
| Segurança de rede | 6/6 fixtures, sem mudanças reais de rede |
| GStreamer/cursor/hooks Win32 | PASS; janela real, foco, release e shutdown; HID fake |
| Pacotes FDD / Portable | PASS: execução direta, ícones SMALL/BIG/Shell, janela dentro da WorkArea e exit 0 |
| Runtime empacotado | 142/142 hashes por variante; versões, HEVC, renderer e porta preservados |

O smoke isolado inicialmente não obteve a janela no prazo de 15 s; na execução normal do desktop os dois mesmos EXEs passaram. Nenhum binário/teste foi alterado para contornar o resultado. Logs de inicialização normais confirmam janela pronta e encerramento limpo.

| Artefato gerado | Bytes | SHA256 |
| --- | ---: | --- |
| `dist/iMirror/iMirror.exe` | 27.395.808 | `45E5527BD615530D8051907736353FEFB0EA91EBF927BEF9368776F1B49E3233` |
| `dist/iMirror-Portable.zip` | 151.821.701 | `B605AA7714712698C1CFB22BD59809E7437F2871B73CA05CC3C025955D39AA3A` |

Evidência local ignorada pelo Git: `logs/ios-stable-debug.log`, `ios-stable-debug-final-build.log`, `ios-stable-release.log`, `ios-stable-package-smoke-user-desktop.log`. Fontes/documentação versionadas; caches, dist, ZIP e logs brutos ficam locais conforme .gitignore. Não foi declarada aprovação física de 10 minutos/reconexão deste release.
