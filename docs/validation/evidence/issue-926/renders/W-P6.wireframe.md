# W-P6 vs docs/media/gui-redesign/W-P6.png

Tolerances: color ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line center offset ≤ 8 px with the same line count.

| region | render color | wireframe color | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #F9F9FA | 0.3 | 6.0 | 1/1 | 0.5 | pass |
| content | #737373 | #FDFDFD | 50.9 | 300.0 | 1/1 | 0.5 | FAIL: color, ink box |
| status line | #F4F4F6 | #B1B1B2 | 24.0 | 79.0 | 1/1 | 0.5 | FAIL: color, ink box |
