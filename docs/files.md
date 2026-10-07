# Arquivos criados

Snapshot histórico das fases0/1. O inventário atual da fase2 e a substituição do stub
AirPlay estão em [phase2-files.md](phase2-files.md).

Inventário da entrega das fases 0 e 1. Os diretórios `bin`, `obj`, `.cache` e
`.tools` contêm resultados locais/dependências de desenvolvimento e são ignorados.

```text
.gitignore
Directory.Build.props
NuGet.Config
global.json
iMirror.sln
README.md

docs/
  research.md
  architecture.md
  phase2-plan.md
  validation.md
  files.md
  images/
    phase1-window.png
    phase1-window-minimum.png

src/
  iMirror.App/
    iMirror.App.csproj
    app.manifest
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    MainViewModel.cs
    RelayCommand.cs
  iMirror.Core/
    iMirror.Core.csproj
    Features/
      FeatureStatus.cs
      IFeatureService.cs
    Diagnostics/
      LogEntry.cs
      IDiagnosticLog.cs
      FileDiagnosticLog.cs
  iMirror.AirPlay/
    iMirror.AirPlay.csproj
    PhaseOneAirPlayService.cs
  iMirror.Bluetooth/
    iMirror.Bluetooth.csproj
    PhaseOneBluetoothService.cs
  iMirror.Input/
    iMirror.Input.csproj
    README.md

tests/
  iMirror.Phase1.Tests/
    iMirror.Phase1.Tests.csproj
    Program.cs

third_party/
  README.md

tools/
  common.ps1
  build.ps1
  run.ps1
  test.ps1
```

Não foi criado instalador, receptor AirPlay, backend BLE ou captura de input.
Os módulos de disponibilidade são explicitamente da fase 1.

Arquivos locais adicionais: script oficial de instalação e SDK em `.tools`,
clones públicos de pesquisa em `.cache/research`, caches de build e evidências em
`.cache/validation`. Não entram nos projetos .NET nem na redistribuição.
