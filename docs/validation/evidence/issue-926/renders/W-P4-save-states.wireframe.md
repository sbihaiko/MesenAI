# W-P4-save-states vs docs/media/gui-redesign/W-P4.png

Tolerances: colour ΔE ≤ 10, ink-box edge offset ≤ 8 px, text-line centre offset ≤ 8 px with the same line count.

| region | render colour | wireframe colour | ΔE | box offset (px) | lines render/wireframe | line offset (px) | verdict |
|---|---|---|---|---|---|---|---|
| title bar | #FAFAFB | #F9F9FA | 0.3 | 6.0 | 1/1 | 0.5 | pass |
| content | #737373 | #FCFCFD | 50.6 | 330.0 | 2/1 | Infinity | FAIL: colour, ink box, text lines |
| status line | #F4F4F6 | #F4F4F6 | 0.0 | 79.0 | 1/1 | 0.5 | FAIL: ink box |
| overlay card | #FAFAFB | #FCFCFD | 0.7 | 2.0 | 12/12 | 57.5 | FAIL: text lines |
| resume button | #FEFEFE | #007AFF | 89.9 | 139.0 | 2/1 | Infinity | FAIL: colour, ink box, text lines |
| grouped rows | #F8F8FA | #FEFEFE | 2.3 | 49.0 | 5/5 | 12.0 | FAIL: ink box, text lines |
