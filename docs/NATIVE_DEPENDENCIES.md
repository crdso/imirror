# Dependências nativas

Em uma máquina já preparada e funcional, não repita instalação, build nativo ou firewall. `.tools` é local e não acompanha o clone.

1. Instale/prepare [MSYS2 x64](https://www.msys2.org/) em `.tools/msys64` dentro do projeto. Complete suas atualizações oficiais e abra **UCRT64**.
2. Instale os pacotes no terminal UCRT64:

   ```bash
   pacman -S --needed mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-cmake mingw-w64-ucrt-x86_64-ninja mingw-w64-ucrt-x86_64-tools mingw-w64-ucrt-x86_64-libplist mingw-w64-ucrt-x86_64-gstreamer mingw-w64-ucrt-x86_64-gst-plugins-base mingw-w64-ucrt-x86_64-gst-plugins-good mingw-w64-ucrt-x86_64-gst-plugins-bad mingw-w64-ucrt-x86_64-gst-libav
   ```

3. Em PowerShell na raiz, execute `tools/build-uxplay.ps1`. Ele fixa UxPlay 1.73.7, valida o MSI Apple e prepara DLL/import library/build local; não instala o serviço Bonjour nem aplica firewall.
4. Para uma instalação nova, execute `tools/finish-airplay-setup-admin.ps1` uma vez como administrador. O script verifica assinatura, configura Bonjour e regras restritas; não desativa o firewall.
5. Execute `tools/prepare-airplay.ps1 -NoLaunch` como usuário normal, seguido do launcher descrito no README. O Doctor aponta versões/plugins/serviço/regras ausentes.

MSYS2 usa pacotes rolling. Uma instalação nova pode obter GStreamer diferente de **1.28.7**, a versão validada neste projeto. O diagnóstico exige essa versão; obter pacotes históricos compatíveis ou validar uma atualização é uma tarefa separada. Não misture DLLs de toolchains diferentes.

`airplay.json` e `profiles/UxPlay-iOS27.json` usam caminhos relativos ao próprio arquivo. O segundo preserva o receiver 1.73.7 e habilita HEVC/debug. Plugins validados localmente incluem `avdec_h264`, `avdec_h265`, `d3d11videosink`, `rtph264depay`, `h264parse` e `decodebin`.

Regras previstas: TCP/UDP **35000–35002** para UxPlay e UDP **5353** para Bonjour, perfil **Private**, origem **LocalSubnet**, por executável. Regras BLOCK existentes para UxPlay podem impedir uma conexão mesmo com discovery funcionando; diagnostique regras reais antes de qualquer mudança.

Fontes: [UxPlay 1.73.7 e instruções Windows](https://github.com/FDH2/UxPlay/blob/v1.73.7/README.md), [manifesto oficial WinGet do Bonjour](https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/Apple/Bonjour/3.1.0.1), [fontes/patch do build externo](../third_party/README.md).
