# W-P4 vs docs/media/gui-redesign/W-P4.png

Tolerances: colour ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line centre offset ≤ 8 px with the same line count.

| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #F9F9FA | 0.3 | 6.0 | 1/1 | 0.5 | pass |
| content | #9E9EA0 | #FCFCFD | 33.8 | 360.0 | 1/1 | 0.5 | FAIL: colour, ink box |
| status line | #F4F4F6 | #F4F4F6 | 0.0 | 79.0 | 1/1 | 0.5 | FAIL: ink box |
| overlay card | #FCFCFD | #FCFCFD | 0.0 | 21.0 | 10/12 | Infinity | FAIL: ink box, text lines |
| resume button | #F9F9FB | #007AFF | 88.2 | 163.0 | 1/1 | 7.0 | FAIL: colour, ink box |
| grouped rows | #FEFEFE | #FEFEFE | 0.0 | 12.0 | 5/5 | 18.0 | FAIL: ink box, text lines |
