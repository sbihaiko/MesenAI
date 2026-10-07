# W-P1 vs docs/media/gui-redesign/W-P1.png

Tolerances: colour ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line centre offset ≤ 8 px with the same line count.

| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #FAFAFB | 0.0 | 6.0 | 1/1 | 0.5 | pass |
| content | #F5F5F7 | #F5F5F7 | 0.0 | 78.0 | 5/6 | Infinity | FAIL: ink box, text lines |
| status line | #F4F4F6 | #F4F4F6 | 0.0 | 973.0 | 1/1 | 0.5 | FAIL: ink box |
| drop block | #F5F5F7 | #F5F5F7 | 0.0 | 21.0 | 4/4 | 20.5 | FAIL: ink box, text lines |
| primary button | #007AFF | #007AFF | 0.0 | 45.0 | 1/1 | 8.5 | FAIL: ink box, text lines |
