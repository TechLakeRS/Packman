# Bundled fonts

The UI prefers Segoe UI Variable / Segoe UI, with Cascadia Mono / Consolas for paths and code. These bundled fallback families are licensed under the
SIL Open Font License 1.1 — https://scripts.sil.org/OFL

- **Instrument Sans** — UI text.
  Copyright 2022 The Instrument Sans Project Authors
  https://github.com/Instrument/instrument-sans
- **JetBrains Mono** — paths, versions, code and other monospaced values.
  Copyright 2020 The JetBrains Mono Project Authors
  https://github.com/JetBrains/JetBrainsMono
- **Martian Mono** — uppercase letterspaced section labels and button text.
  Copyright 2020 The Martian Mono Project Authors
  https://github.com/evilmartians/mono

The upstream releases are variable fonts. WPF cannot interpolate a variable
axis, so each family is shipped as four static instances (400/500/600/700)
cut from the `wght` axis. Family and typographic-family name records are set
so `FontFamily="Instrument Sans"` plus `FontWeight="Medium"` resolves — the
non-RIBBI weights carry name IDs 16/17.

Coverage is latin + latin-ext.
