# Validação do redesign e release

Data: 2026-10-06. Este resumo contém somente resultados técnicos; logs brutos e screenshots com caminhos do usuário ficam fora do Git.

| Verificação | Resultado |
|---|---|
| Debug / Release | 0 erros / 0 warnings em ambos |
| Suites completas em cada configuração | Fase 1: 8/8 grupos; Fase 2: 10/10; Fase 3: 32/32 |
| Probe BLE self-test | 11/11, sem acesso Bluetooth |
| Fixtures de rede | 6/6, sem alterar a rede física |
| UxPlay mínimo pela UI | 5/5 Start → Wait → Stop; anúncio local, stdout/stderr, duplicação e ausência de órfãos |
| Renderer | H.264 e H.265 sintéticos: janela visível, frames renderizados, EOS e exit 0 |
| Viewports WPF | 1366×768, 1920×1080 e 2560×1440; todas as cinco páginas renderizadas sem o limite do HWND do monitor |
| Diagnóstico | Oculto por padrão; abre/fecha; filtros; lista renderiza; Limpar preserva o arquivo |
| Rotação | Tamanho, cinco arquivos, múltiplas sessões, UTF-8 e preservação de arquivo alheio verificados |
| EXE direto | Framework-dependent e self-contained abriram e fecharam com exit 0, sem dotnet run |
| Cursor nativo desta execução | Criação/remoção da superfície e fail-open passaram; ativação dos hooks reais ficou SKIP porque o Windows negou foreground ao teste em background |

O teste anterior de cursor com foreground adquirido permanece documentado em PHASE3B_VALIDATION.md. Nenhum report HID foi enviado ao iPhone nos testes sintéticos. Não houve nova validação física nesta sessão.

## Runtime

Imports PE normais e delay imports x64, gst-inspect e módulos realmente carregados foram analisados. O pacote usa 142 arquivos nativos, com plugins necessários à recepção, typefindfunctions e autodetect. O código upstream verifica autodetect mesmo com sink explícito. A falta desse plugin foi identificada no teste de startup, corrigida no empacotamento e seguida por cinco ciclos aprovados.

O runtime instalado conserva dois plugins de encoder somente para os testes. O release não os inclui. Compiladores, headers, bibliotecas estáticas, Python, pacman e caches MSYS2 foram removidos. UxPlay, DLLs selecionadas e os perfis mantiveram seus hashes. Rollback de 821 arquivos em .tools/rollback foi verificado por SHA256 antes da redução.

Não foram alterados AirPlay negotiation, BLE/HOGP, Report Map, input, renderer, Bonjour, Wi-Fi, WSL ou firewall. Fonte GPL correspondente ao UxPlay 1.73.7 e patch foram preservados localmente; receitas/licenças estão documentadas.

PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED
PHASE 3 INPUT: PHYSICALLY VALIDATED
PHASE 3 UX: UPDATED — PENDING USER VALIDATION
