# Packman conventions - read first

**Packman ships no components.** It is a Windows desktop app (WPF, Fluent look) and this
design system is its *design language only*: colour roles for two themes, type styles,
per-control recipe tokens, three bundled font families and 25 icon geometries. `window.Packman`
is intentionally empty. Build every screen from plain HTML/React elements and style them with
the `--pm-*` custom properties below. Ignore any "React library" wording further down.

## Setup

- Link `styles.css` once. No provider, no wrapper. It sets the page to the app's default
  surface, ink and typeface (`body { background: var(--pm-bg); color: var(--pm-text);
  font-family: var(--pm-font-ui) }`).
- **Dark is the default** (`:root`). For the light theme put `data-theme="light"` on the
  root or on any container; every `--pm-*` colour re-resolves underneath it. Never hard-code
  a hex: most surfaces are translucent layers (`rgb(255 255 255 / 0.059)`) that only look
  right over `--pm-bg` / `--pm-shell`.
- Text colour must come from the ink ramp (`--pm-ink` > `--pm-text` > `--pm-text-2` >
  `--pm-muted` > `--pm-muted-2` > `--pm-muted-3` > `--pm-dim`). `--pm-mark` is for dots,
  chevrons and rules only, never text.

## Styling idiom: `var(--pm-*)`, nothing else

There are no utility classes and no component classes. Three token files, all reachable
from `styles.css`:

| File | What it holds | Examples |
|---|---|---|
| `tokens/colors.css` | 48 colour roles x 2 themes | surfaces `--pm-bg`, `--pm-shell`, `--pm-surface`, `--pm-flyout`, `--pm-card`, `--pm-card-2`, `--pm-rail`; strokes `--pm-line`, `--pm-line-2`, `--pm-edge`, `--pm-control-stroke`; fields `--pm-field`, `--pm-control`, `--pm-control-hover`; accent `--pm-primary`, `--pm-primary-2`, `--pm-primary-fg`, `--pm-primary-soft`, `--pm-primary-line`, `--pm-accent-text`; states `--pm-ok`, `--pm-warn`, `--pm-bad` with `-soft` fills and `-line` strokes (`--pm-ok-soft`, `--pm-ok-line`); code `--pm-code-bg`, `--pm-code-txt`, `--pm-code-bar-txt`, `--pm-code-comment` |
| `tokens/typography.css` | font stacks + text styles | `--pm-font-ui`, `--pm-font-mono`, `--pm-font-label`; per style `-family`/`-size`/`-weight`/`-color`: `--pm-h1-*` (20/600), `--pm-h2-*` (14/600), `--pm-sub-text-*` (13, line-height 20), `--pm-section-label-*` (11/600, label face, typed in CAPS), `--pm-caption-*` (mono 11), `--pm-field-label-*` (11.5/500), `--pm-col-head-*` (10/600) |
| `tokens/components.css` | recipe per control style | `--pm-primary-button-*`, `--pm-ghost-button-*`, `--pm-mini-button-*`, `--pm-icon-button-*`, `--pm-text-field-*`, `--pm-mono-field-*`, `--pm-nav-item-*`, `--pm-page-tab-*`, `--pm-card-*`, `--pm-focus-visual-*` - each with `-bg`, `-color`, `-border-color`, `-border-width`, `-radius`, `-padding`, `-min-height`/`-height`, `-size`, `-weight` as the source style defines them |

Rules that follow from the source:

- **Accent is amber and it is the only brand colour.** Filled accent = `--pm-primary` with
  `--pm-primary-fg` text. Amber as text, icon or hairline = `--pm-accent-text` (darker in
  light theme so it stays legible). Warnings are `--pm-warn`, never amber.
- **Radii**: cards 8px (`--pm-card-radius`), buttons and fields 4px, nav items 5px. Separation is a layer plus a 1px `--pm-line` stroke, **no shadows**.
- **Sizes**: buttons 34px min height (`--pm-primary-button-min-height`), mini buttons 26px,
  text fields 36px (`--pm-text-field-height`), nav rows 38px. Card padding is
  `--pm-card-padding` (20px 22px). No spacing scale exists; the app's gaps are mostly
  6 to 18px, with 8, 12 and 14 the most common.
- **Section headers** are an uppercase `--pm-section-label-*` caption followed by a 1px
  `--pm-rule-bg` rule that fills the row, with an optional mono `--pm-caption-*` on the right.
- **Keyboard focus** is a 2px amber ring outside the control: `outline: var(--pm-focus-visual-ring);
  outline-offset: var(--pm-focus-visual-offset)`, `:focus-visible` only.
- **Icons**: inline SVG, 24x24 viewbox, `fill="none"`, `stroke="currentColor"`,
  `stroke-width` 1.8, round caps and joins. Path data for all 25 is in
  `guidelines/docs/icons.md` (`IconFolder`, `IconUpload`, `IconCheck`, `IconChevR`, ...).
- Monospace (`--pm-font-mono`) is for paths, versions, product codes and script text.

## Idiomatic snippet

```jsx
<section style={{ background: 'var(--pm-card)', border: 'var(--pm-card-border-width) solid var(--pm-card-border-color)',
                  borderRadius: 'var(--pm-card-radius)', padding: 'var(--pm-card-padding)' }}>
  <header style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
    <span style={{ fontFamily: 'var(--pm-section-label-family)', fontSize: 'var(--pm-section-label-size)',
                   fontWeight: 'var(--pm-section-label-weight)', color: 'var(--pm-section-label-color)' }}>APPLICATION INFORMATION</span>
    <span style={{ flex: 1, height: 'var(--pm-rule-height)', background: 'var(--pm-rule-bg)' }} />
  </header>
  <label style={{ display: 'block', margin: '16px 0 7px', fontSize: 'var(--pm-field-label-size)',
                  fontWeight: 'var(--pm-field-label-weight)', color: 'var(--pm-field-label-color)' }}>Sources Path</label>
  <input value="\\share\apps\7zip\24.09\7z2409-x64.msi" readOnly
         style={{ width: '100%', boxSizing: 'border-box', height: 'var(--pm-text-field-height)', padding: 'var(--pm-text-field-padding)',
                  background: 'var(--pm-text-field-bg)', color: 'var(--pm-text-field-color)', fontFamily: 'var(--pm-font-mono)',
                  border: 'var(--pm-text-field-border-width) solid var(--pm-text-field-border-color)', borderRadius: 'var(--pm-text-field-radius)' }} />
  <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
    <button style={{ background: 'var(--pm-primary-button-bg)', color: 'var(--pm-primary-button-color)', border: 0,
                     minHeight: 'var(--pm-primary-button-min-height)', padding: 'var(--pm-primary-button-padding)',
                     borderRadius: 'var(--pm-primary-button-radius)', fontWeight: 'var(--pm-primary-button-weight)',
                     fontSize: 'var(--pm-primary-button-size)' }}>GENERATE PACKAGE</button>
    <button style={{ background: 'var(--pm-ghost-button-bg)', color: 'var(--pm-ghost-button-color)',
                     border: 'var(--pm-ghost-button-border-width) solid var(--pm-ghost-button-border-color)',
                     minHeight: 'var(--pm-ghost-button-min-height)', padding: 'var(--pm-ghost-button-padding)',
                     borderRadius: 'var(--pm-ghost-button-radius)', fontWeight: 'var(--pm-ghost-button-weight)',
                     fontSize: 'var(--pm-ghost-button-size)' }}>Browse</button>
  </div>
</section>
```
