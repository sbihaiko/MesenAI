# W-P7 vs docs/media/gui-redesign/W-P7.png

Tolerances: colour ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line centre offset ≤ 8 px with the same line count.

| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #F9F9FA | 0.3 | 6.0 | 1/1 | 0.5 | pass |
| content | #737373 | #FBFBFC | 50.2 | 320.0 | 3/1 | Infinity | FAIL: colour, ink box, text lines |
| status line | #F4F4F6 | #B1B1B2 | 24.0 | 79.0 | 1/1 | 0.0 | FAIL: colour, ink box |
