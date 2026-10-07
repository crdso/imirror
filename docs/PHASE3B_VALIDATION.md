# Validação local — Fase 3B

Revalidação de conexão atual: [iOS Stable / Known Good](IOS_STABLE_HID_REVALIDATION.md). O registro abaixo é histórico; o schema estendido ABNT2 foi substituído pelo descriptor estável de 0x65.

Data: **2026-10-06**. Resumo técnico sanitizado; logs brutos e screenshots locais não são publicados.

| Verificação | Resultado |
| --- | --- |
| Build Debug | 0 erros / 0 warnings |
| Build Release | 0 erros / 0 warnings |
| Suite Fase 1, Debug e Release | 6/6 grupos em cada configuração |
| Suite Fase 2, Debug e Release | 10/10 grupos em cada configuração |
| Suite Fase 3, Debug e Release | 32/32 testes em cada configuração |
| Probe BLE isolado | 11/11 self-tests, sem acesso Bluetooth |
| Fixtures de segurança de rede | 6/6 grupos; nenhuma chamada à rede física |
| Renderer sintético | d3d11videosink iniciou com videotestsrc |
| Cursor nativo | GetCursorInfo confirmou ocultação, saída, reentrada e restauração após Stop |
| Foreground e hooks | Janela do receiver permaneceu foreground; hooks reais instalados/removidos; reativação passou |
| HID no teste nativo | Transporte falso; nenhum report enviado ao iPhone |
| UxPlay e configurações AirPlay | SHA256 iguais aos checkpoints anteriores |

Os testes novos cobrem velocidade linear/resto/clamp, HKL, scan codes OEM/ABNT2, cada símbolo solicitado, acentos/dead keys com releases, discard na saída do viewport, reentrada rápida, envio pendente na parada e ausência de novos envios após conclusão.

A tentativa de alterar a classe de cursor do processo GStreamer retornou **Access Denied**. A correção usa uma superfície Win32 do iMirror, sem ativação, criada como topmost e limitada ao viewport. O teste nativo comprovou o efeito; não há contadores ShowCursor ou alteração de cursores do sistema.

O único ajuste no Report Map é ampliar o intervalo do teclado para International1 0x87, necessário à tecla extra ABNT2. O restante é comparado com a fixture upstream original. Símbolos/acentos no layout de hardware do iOS e eventual cache do descriptor ainda exigem teste do usuário.

**Validação física previamente informada pelo usuário:** iPhone 14/iOS 27.0.1, pairing, mouse/keyboard subscribers, ponteiro AssistiveTouch, movimento, clique, letras básicas e AirPlay simultâneo.

- **PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED**
- **PHASE 3 INPUT: PHYSICALLY VALIDATED**
- **PHASE 3 UX: UPDATED — PENDING USER VALIDATION**

Wheel, reconexão e rotação não receberam nova aprovação física nesta execução. Consulte [roteiro da UX](PHASE3_CONTROL.md).
