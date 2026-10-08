# W-P2 vs docs/media/gui-redesign/W-P2.png

Tolerances: color ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line center offset ≤ 8 px with the same line count.

| region | render color | wireframe color | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #FAFAFB | 0.0 | 6.0 | 1/1 | 0.5 | pass |
| content | #F6F6F8 | #F7F7F8 | 0.6 | 8.0 | 5/5 | 12.0 | FAIL: text lines |
| status line | #F4F4F6 | #F4F4F6 | 0.0 | 973.0 | 1/1 | 0.5 | FAIL: ink box |
| continue card | #FEFEFE | #FEFEFE | 0.0 | 76.0 | 1/1 | 2.0 | FAIL: ink box |
| continue button | #007AFF | #007AFF | 0.0 | 3.0 | 1/1 | 0.0 | pass |
| recent tiles | #F5F5F7 | #F4F4F6 | 0.3 | 395.0 | 2/2 | 6.5 | FAIL: ink box |
