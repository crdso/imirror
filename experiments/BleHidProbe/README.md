# Probe BLE HID de diagnóstico

Abra **Testar-BLE-HID.cmd**, com Bluetooth ligado e BLE do iMirror desconectado. Console separado, sem hooks, compartilhando o transporte **src/iMirror.Bluetooth**. Named semaphore impede dois servidores simultâneos. iMirror é a solução de uso normal.

HID completo: Information, Report Map, Control Point, Protocol Mode (default report=1), reports ID1/8 bytes e ID2/6 bytes, Report References, criptografia, CCCD do Windows. Battery 0x180F/100%. Boot reports separados; boot mouse sem wheel. Report Map upstream exato (MIT).

No iPhone, **Ajustes > Bluetooth**, toque no PC. Para cursor: **Ajustes > Acessibilidade > Toque > AssistiveTouch**. Teclado não exige AssistiveTouch. Nome BLE é a identidade Bluetooth do Windows.

Comandos: status; move dx dy; click left; click right; scroll n; key usage; type texto; release; exit. key 0x04 = a, key 0x28 = Enter. type aceita 1–120 caracteres ASCII com layout físico US; valida tudo antes de enviar. Presses têm release em finally. Sem subscriber/link, envio bloqueado.

status mostra advertising, sessões, bond, reads, Protocol Mode e subscriptions. Notify Success comprova transporte; observe fisicamente o efeito. mark pairing|mouse|click|wheel|keyboard|reconnection pass|fail registra a observação do operador. Ctrl+C/exit fazem cleanup; kill forçado não garante release.

tools/start-ble-hid-probe.ps1 -SelfTest executa 11 testes sem Bluetooth; -Build recompila. Console espera rádio ligado sem alterá-lo. Log logs/ble-hid-probe.log. Não exige administrador.

[Integração e limites](../../docs/PHASE3_CONTROL.md); [MIT](../../src/iMirror.Bluetooth/THIRD_PARTY_NOTICES.md).

**PHASE 3: IMPLEMENTED — PENDING PHYSICAL VALIDATION**

