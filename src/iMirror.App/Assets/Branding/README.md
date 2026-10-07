# Assets fornecidos pelo usuário

- `iMirror.png`: https://i.imgur.com/8yxtBFa.png — identidade azul do aplicativo.
- `iPhone.png`: https://i.imgur.com/frvC1zV.png — identidade vermelha do renderer externo.

As imagens originais ficam locais; não há download em runtime. Os ICOs são gerados por `tools/generate-branding-icons.ps1` com frames PNG de 16/20/24/32/40/48/64/128/256 px, preservando transparência. PNG/ICO são resources embutidos e o ICO azul também alimenta ApplicationIcon do EXE.
