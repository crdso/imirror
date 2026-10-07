# Uso de disco antes da redução

Medição em 2026-10-06 22:03:58. Total de arquivos: **4.168 GiB (4475012160 bytes)**. Pastas abaixo são cumulativas: não somar linhas sobrepostas.

| Item | Tipo | MiB | Classificação / decisão |
|---|---|---:|---|
| .tools | folder | 3823.04 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\msys64 | folder | 3003.2 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\msys64\ucrt64 | folder | 2433.82 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\msys64\ucrt64\lib | folder | 1345.99 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\dotnet | folder | 769.94 | dependency / runtime required + SDK; NOT safe to delete (build local) |
| .tools\msys64\ucrt64\bin | folder | 671.3 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\dotnet\sdk\10.0.401 | folder | 399.01 | dependency / runtime required + SDK; NOT safe to delete (build local) |
| .tools\dotnet\sdk | folder | 399.01 | dependency / runtime required + SDK; NOT safe to delete (build local) |
| .tools\msys64\var | folder | 347.68 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\msys64\var\cache | folder | 339.55 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\msys64\var\cache\pacman\pkg | folder | 339.55 | cache / build artifact / safe to delete |
| .tools\msys64\var\cache\pacman | folder | 339.55 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .cache | folder | 233.12 | dependency / mixed runtime + development; NOT safe to delete integralmente antes da validação |
| .tools\msys64\ucrt64\share | folder | 229.08 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\msys64\usr | folder | 220.07 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\msys64\ucrt64\lib\python3.14 | folder | 214.52 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\dotnet\shared | folder | 199.15 | dependency / runtime required + SDK; NOT safe to delete (build local) |
| .tools\msys64\ucrt64\include | folder | 166.44 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\msys64\ucrt64\lib\librsvg-2.a | file | 162.57 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\dotnet\packs | folder | 160.84 | dependency / runtime required + SDK; NOT safe to delete (build local) |
| .tools\msys64\ucrt64\lib\python3.14\test | folder | 148.35 | dependency / development-only; remover apenas após validar runtime mínimo |
| .cache\validation | folder | 135.93 | cache / build artifact / safe to delete |
| .tools\msys64\ucrt64\lib\gcc | folder | 125.52 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\msys64\ucrt64\lib\gcc\x86_64-w64-mingw32 | folder | 125.52 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\msys64\ucrt64\lib\gcc\x86_64-w64-mingw32\16.2.0 | folder | 125.52 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\msys64\usr\bin | folder | 110.75 | dependency / development-only; remover apenas após validar runtime mínimo |
| tests | folder | 102.94 | source + build artifact; preservar source, limpar bin/obj |
| .tools\msys64\usr\share | folder | 101.36 | dependency / development-only; remover apenas após validar runtime mínimo |
| .tools\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.12 | folder | 94.37 | dependency / runtime required + SDK; NOT safe to delete (build local) |
| .tools\dotnet\shared\Microsoft.WindowsDesktop.App | folder | 94.37 | dependency / runtime required + SDK; NOT safe to delete (build local) |

## Estimativa antes da extração
Causas: MSYS2 inteiro 3.003 MiB, SDK .NET local 770 MiB, cache/evidências 233 MiB, outputs de testes duplicados. Cache pacman 340 MiB pode ser removido já, sem afetar executáveis instalados.
Potencial estimado: 2–3 GiB após provar o runtime mínimo; não é uma promessa de remoção de DLLs. Desenvolvimento: preservar SDK .NET ~770 MiB, cache NuGet de referências WinRT e fontes/testes; MSYS2 compilador só é necessário para recompilar UxPlay, não para WPF. Distribuição: estimar 200–500 MiB até medir a closure das DLLs/plugins. Não inclui SDK/compiladores/headers/caches.
Runtime atual permanece intacto até cópia enxuta passar gst-inspect, renderer e ciclos UxPlay; rollback do runtime será preservado. Nenhuma pasta externa será limpa.
