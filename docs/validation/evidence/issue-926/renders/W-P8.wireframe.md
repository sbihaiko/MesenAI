# W-P8 vs docs/media/gui-redesign/W-P8.png

Tolerances: color ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line center offset ≤ 8 px with the same line count.

| region | render color | wireframe color | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #000000 | #F9F9FA | 98.0 | 23.0 | 1/1 | 13.5 | FAIL: color, ink box, text lines |
| content | #737373 | #FCFCFC | 50.5 | 310.0 | 2/1 | Infinity | FAIL: color, ink box, text lines |
| status line | #747474 | #B1B1B2 | 23.4 | Infinity | 0/1 | Infinity | FAIL: color, ink box, text lines |
