# Release do polimento final

Registro histórico do código `a952834`. Os pacotes locais foram atualizados depois; hashes atuais e diagnóstico da reconexão estão em [BLUETOOTH_CONNECTION_INVESTIGATION.md](BLUETOOTH_CONNECTION_INVESTIGATION.md).

Data: 2026-10-07. Base anterior: `8635a8a`; código publicado: `a952834`. Código e documentação versionados; EXE, ZIP e evidências brutas permanecem locais, ignorados pelo Git.

## Pacotes

| Artefato | Bytes | MiB | SHA256 |
|---|---:|---:|---|
| dist/iMirror/iMirror.exe | 27.337.952 | 26,07 | `6C721E55278E25BDC15BE6E3F6693F542104EC422ECF65CD047B7F283C6BCE25` |
| dist/iMirror-Portable.zip | 151.801.744 | 144,77 | `C10AA55BE08FF983B9DB8F92E249ADAFEB0399497BBEF765F0C7B4936CAE33AA` |

Diretório framework-dependent completo: 211.374.079 bytes (201,58 MiB), requer Desktop Runtime .NET 10 x64, ou reutiliza o SDK local no checkout. O ZIP self-contained tem 383.711.903 bytes descompactados (365,94 MiB), inclui .NET/WPF/Windows Forms para a bandeja e não exige SDK instalado. Ambos incluem o runtime AirPlay de 184.030.660 bytes (175,51 MiB), sem SDK, MSYS2 completo, fonte, obj, cache ou logs temporários.

Smoke test: `tools/test-release-branding.ps1`, executado na sessão interativa. Ambos os EXEs abriram diretamente e fecharam com exit 0; janela 956×582 dentro da WorkArea; ícones SMALL/BIG e Shell azuis por pixels; atalho local temporário também azul. ZIP extraído em diretório independente dentro do workspace autorizado; resources funcionaram sem arquivos de Assets na pasta do EXE. PATH limitado ao Windows e DOTNET_ROOT removido. O teste não pareia nem envia input ao iPhone. A janela Gst real recebeu ícones vermelhos e título pelo teste de renderer em cada configuração.

## Preservação e validação

- Debug/Release: 0 warnings, 0 errors, 58/58 grupos por configuração (UI 10, AirPlay 10, input/BLE 38).
- Probe: 11/11; fixtures de segurança de rede: 6/6. Nenhuma mudança de rede aplicada.
- Cursor nativo: PASS com HID fake. Renderer nativo: videotestsrc, aspect portrait/landscape, resize manual, auto-size e foco/retorno.
- BLE real local: advertising Started; Connect repetido e timeout de 31s mantiveram geração 1; nenhum input enviado. Cleanup solicitou StopAdvertising para BAS/HID, mas Windows ainda reportou Started após espera limitada. PASS nativo: reset interno e novo Connect mantiveram geração 1 sem outro provider quando a parada não foi confirmada; a UI orienta fechar/reabrir o app. Reconexão continua pendente do iPhone; não se promete remoção de cache/bond/registro durante reset.
- 142 arquivos nativos conferidos por SHA256 em cada pacote. UxPlay original preservado: `BBEDA5A9757178BC99186F7CA58C80A9ABD47A7EA2DED8043EEAA532B75FD33E`.
- airplay.json original: `4530C757E2E7A2A428DA8385C0F61CD2CE9652FE8DF7680496464BD6EF159AF2`; perfil UxPlay-iOS27 original: `E2D9C6511C5F00EAB9A1D64F08AB01A12267C433CFEA95B0B549F075623F5D7B`.
- UxPlay 1.73.7, GStreamer 1.28.7, HEVC, d3d11videosink e portas 35000–35002 preservados. Bonjour, firewall, WSL e Wi-Fi não foram alterados. Em outra pasta/PC, autorização de firewall do caminho distribuído pode continuar necessária.

Logs de evidência ignorados: `logs/final-polish-debug.log`, `logs/final-polish-release.log`, `logs/final-polish-native-hid-test.log`, `logs/final-polish-package-smoke.log`. Procedimento físico e limitações: [FINAL_POLISH_VALIDATION.md](FINAL_POLISH_VALIDATION.md).

## Arquivos alterados nesta sessão

### Aplicação, janelas e assets

- src/iMirror.App/iMirror.App.csproj
- src/iMirror.App/MainWindow.xaml
- src/iMirror.App/MainWindow.xaml.cs
- src/iMirror.App/MainViewModel.cs
- src/iMirror.App/MainViewModel.Presentation.cs
- src/iMirror.App/DiagnosticVisibility.cs
- src/iMirror.App/Views/ControlView.xaml
- src/iMirror.App/Views/MirrorView.xaml
- src/iMirror.App/Views/SettingsView.xaml
- src/iMirror.App/Presentation/BrandingIcons.cs
- src/iMirror.App/Presentation/RendererPresentation.cs
- src/iMirror.App/Presentation/WindowGeometry.cs
- src/iMirror.App/Presentation/WindowNative.cs
- src/iMirror.App/Presentation/WindowPreferences.cs
- src/iMirror.App/Presentation/WindowPresentation.cs
- src/iMirror.App/Assets/Branding/README.md
- src/iMirror.App/Assets/Branding/iMirror.png
- src/iMirror.App/Assets/Branding/iMirror.ico
- src/iMirror.App/Assets/Branding/iPhone.png
- src/iMirror.App/Assets/Branding/iPhone.ico

### Bluetooth e captura

- src/iMirror.Bluetooth/BluetoothContracts.cs
- src/iMirror.Bluetooth/BluetoothController.cs
- src/iMirror.Bluetooth/BluetoothControlLog.cs
- src/iMirror.Bluetooth/HogpPeripheral.cs
- src/iMirror.Bluetooth/PairingStatus.cs
- src/iMirror.Input/InputCapture.cs
- src/iMirror.Input/InputModel.cs

### Testes, ferramentas e documentação

- tests/iMirror.Phase1.Tests/Program.cs
- tests/iMirror.Phase1.Tests/FinalPolishProbe.cs
- tests/iMirror.Phase3.Tests/Program.cs
- tests/iMirror.Phase3.Tests/HogpLifecycleProbe.cs
- tools/generate-branding-icons.ps1
- tools/test-release-branding.ps1
- README.md
- docs/RELEASE.md
- docs/FINAL_POLISH_VALIDATION.md
- docs/FINAL_POLISH_RELEASE.md

READY FOR FINAL PHYSICAL VALIDATION
