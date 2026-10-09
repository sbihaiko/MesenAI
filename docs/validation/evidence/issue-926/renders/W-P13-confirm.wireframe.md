# W-P13-confirm vs docs/media/gui-redesign/W-P13.png

Tolerances: color ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line center offset ≤ 8 px with the same line count.

| region | render color | wireframe color | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #F9F9FA | 0.3 | 6.0 | 1/1 | 0.5 | pass |
| content | #9E9EA0 | #B2B2B3 | 7.5 | 271.0 | 1/2 | Infinity | FAIL: ink box, text lines |
| status line | #F4F4F6 | #B1B1B2 | 24.0 | 866.0 | 1/1 | 0.5 | FAIL: color, ink box |
