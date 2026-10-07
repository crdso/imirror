# Validação — fases 0 e 1

Data: 05/10/2026. Ambiente observado: Windows x64 build 19045 (Windows 10),
SDK .NET 10.0.401, runtime/Windows Desktop 10.0.12.
SDK local em `.tools/dotnet`; sem instalação global/PATH/registro.

## Resultados

| Verificação | Resultado |
| --- | --- |
| Solução com cinco módulos + projeto de testes | Compila |
| Build Debug | 0 avisos, 0 erros |
| Build Release | 0 avisos, 0 erros |
| Harness Release `tools/test.ps1 -Configuration Release` | 4 grupos aprovados, exit code 0 |
| 620 escritas concorrentes em log | Todas no arquivo; 500 em memória; 620 eventos |
| Escrita depois de Dispose / Dispose repetido | Escrita rejeitada; descarte idempotente |
| Caminho de log inválido | IOException propagada, sem sucesso falso |
| Janela WPF real e bindings dos três comandos | Validados |
| AirPlay/Bluetooth | Avisos explícitos; sem conexão simulada |
| F11 / ESC / estado maximizado anterior | Ativa, restaura e preserva estado anterior |
| Log vindo de thread auxiliar | Atualiza painel por Dispatcher |
| Dispose do ViewModel | Remove observador de logs |
| Executável em processo separado | Cria janela `iMirror`, aceita WM_CLOSE, exit code 0 |
| Logs do executável | Início, janela pronta, encerramento; sem nível Error |
| Layout padrão e mínimo | PNGs renderizados do conteúdo WPF e conferidos visualmente |

A última execução aprovada produziu evidências em
`.cache/validation/20261005-195841-914`: histórico, log do processo real e imagens.
As imagens foram preservadas em [janela padrão](images/phase1-window.png) e
[janela mínima](images/phase1-window-minimum.png). São renders do WPF em teste,
sem moldura nativa, não capturas de tela do iPhone.

## Correções durante a validação

Foi detectada reentrância na rolagem do ListBox durante `CollectionChanged`.
A correção agenda/coalesce a rolagem no Dispatcher após os consumidores
processarem o evento. O cenário de dois avisos consecutivos integra o teste.
O leitor de evidência de log usa FileShare.ReadWrite para ler durante gravação.

No sandbox, o SDK tentou ler o perfil NuGet Windows e encontrou acesso negado,
mesmo com configuração de restore explícita. Build/test foram realizados com
acesso ao perfil, usando a configuração local sem feeds. Nenhum pacote externo
foi necessário. Isso é uma limitação desse ambiente de execução, não dependência
de administrador do aplicativo (manifesto asInvoker).

## Limites do que foi validado

- Windows 11, ARM64, múltiplos monitores e mudança entre DPIs não foram testados.
- Não foi feito teste de pareamento, descoberta AirPlay, tela, rotação do iPhone,
  Bluetooth, cursor, mouse ou teclado remoto: esses recursos não existem aqui.
- Os testes verificam comandos/bindings e eventos sintéticos locais de teclado;
  não substituem uma sessão manual extensa de uso da interface.
- Fase 0 documentada; fase 1 compilada e validada localmente. Fase 2 **não iniciada**.

Para reproduzir: executar `tools/test.ps1 -Configuration Release` numa sessão
desktop Windows com SDK .NET 10. O harness retorna código 1 se qualquer grupo
falhar. Consultar o README para execução da janela e pasta de logs.
