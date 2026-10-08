# Input

Hooks temporários em MTA e sender separado para a janela Gst do receiver deste iMirror. Enquanto ativo, Raw Input fornece movimento relativo físico; o cursor local é oculto e confinado a um ponto interno do viewport, calculado com aspect ratio/letterboxing/DPI. Resize/rotação atualizam esse ponto sem enviar deslocamento ao iPhone. Não multiplica deltas pela resolução do stream. Esc, foco perdido, desconexão e shutdown liberam ClipCursor imediatamente, antes de aguardar releases BLE, e removem o registro Raw Input. Teclado HID, wheel, pacing e releases mantidos. Sem captura permanente ou log de teclas. [Correção do mouse](../../docs/MOUSE_CAPTURE_FIX.md).

