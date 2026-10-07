# W-P15-pill vs docs/media/gui-redesign/W-P15.png

Tolerances: colour ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line centre offset ≤ 8 px with the same line count.

| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #0B1430 | #0A0E20 | 9.1 | Infinity | 0/1 | Infinity | FAIL: ink box, text lines |
| content | #0B1430 | #FAFAFC | 93.4 | 645.0 | 1/1 | 322.5 | FAIL: colour, ink box, text lines |
| status line | #0B1430 | #32190E | 32.7 | Infinity | 0/1 | Infinity | FAIL: colour, ink box, text lines |
| setup sheet | #0B1430 | #FAFAFC | 93.4 | Infinity | 0/11 | Infinity | FAIL: colour, ink box, text lines |
