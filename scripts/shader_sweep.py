#!/usr/bin/env python3
"""Manual sweep of RetroArch .slangp presets through the macOS Metal presenter.

ADR-0237. macOS only, needs a Metal device. A maintainer tool, NOT a test and
NOT wired into CI or doc-checks: GPU results depend on the machine. Full
contract and how to read the output: docs/shader-sweep.md.

For every preset it runs, each in its own process with a timeout:
  render  scripts/shader_sweep_shot (MacOS/MetalPresenter.mm + librashader) on
          a reference frame for --nframes frames, read back and compared with
          the unfiltered picture;
  params  GetShaderParams from MesenCore.dylib through ctypes, the two calls
          UI/Interop/ConfigApi.cs makes (count, then list).
It writes sweep.tsv and summary.txt and exits 1 when any process crashed or
timed out, 2 on a setup error (missing dylib, failed control run).

usage: shader_sweep.py <shaders-dir> [--out DIR] [--jobs N] [--timeout S]
       [--nframes N] [--scale N] [--frame PNG] [--glob PAT]... [--sample N]
       [--preset REL]... [--keep-images] [--no-params] [--no-caffeinate] [--no-recheck]
"""
import argparse
import ctypes
import fnmatch
import glob
import hashlib
import os
import re
import signal
import subprocess
import sys
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HARNESS = os.path.join(ROOT, "scripts", "shader_sweep_shot")
FAILING = ("CRASH", "TIMEOUT")
LINE = re.compile(r"^SWEEP set=(\d) present=(\d) w=(\d+) h=(\d+) diffpx=(\d+) black=([\d.]+) mean=([\d.]+) err=(.*)$", re.M)


class ParamDef(ctypes.Structure):
    # Utilities/Video/LibrashaderUtilities.h ShaderParamDefinition
    _fields_ = [("Name", ctypes.c_char * 200), ("Description", ctypes.c_char * 200), ("Min", ctypes.c_double),
                ("Max", ctypes.c_double), ("Initial", ctypes.c_double), ("Step", ctypes.c_double)]


def params_one(core, preset):
    """Child mode: one preset's GetShaderParams, printed as one line."""
    lib = ctypes.CDLL(core)
    lib.CheckShaderSupport.restype = ctypes.c_bool
    lib.GetShaderParams.restype = ctypes.c_uint32
    lib.GetShaderParams.argtypes = [ctypes.c_char_p, ctypes.c_void_p]
    count = lib.GetShaderParams(preset.encode(), None)
    buf = (ParamDef * max(count, 1))()
    listed = lib.GetShaderParams(preset.encode(), ctypes.cast(buf, ctypes.c_void_p)) if count else 0
    first = buf[0].Name.decode("utf-8", "replace") if listed else "-"
    print(f"PARAMS support={int(lib.CheckShaderSupport())} count={count} list={listed} first={first}")


def run(cmd, cwd, timeout):
    """(rc, stdout, stderr, secs); rc None = timeout. Own session so a hang is killed whole."""
    t = time.time()
    p = subprocess.Popen(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                         errors="replace", start_new_session=True)
    try:
        out, err = p.communicate(timeout=timeout)
        return p.returncode, out, err, time.time() - t
    except subprocess.TimeoutExpired:
        os.killpg(p.pid, signal.SIGKILL)
        out, err = p.communicate()
        return None, out, err, time.time() - t


def crash_detail(rc, err, out):
    sig = f"signal {-rc} ({signal.Signals(-rc).name})" if rc < 0 else f"rc={rc}"
    tail = (err.strip().splitlines() or out.strip().splitlines() or [""])[-2:]
    return f"{sig}: {' | '.join(tail)}"[:400]


def render(a, preset, image, nframes=None, timeout=None):
    timeout = timeout or a.timeout
    cmd = [HARNESS, a.frame, str(a.scale), str(nframes or a.nframes), preset] + ([image] if image else [])
    rc, out, err, secs = run(cmd, a.libdir, timeout)
    if rc is None:
        return "TIMEOUT", f"no result after {timeout}s", secs, "-"
    if rc != 0:
        return "CRASH", crash_detail(rc, err, out), secs, str(rc)
    m = LINE.search(out)
    if not m:
        return "CRASH", "no SWEEP line: " + out.strip()[-200:], secs, "0"
    st, pr, w, h, diff, black, mean, msg = m.groups()
    if st == "0":
        if msg.startswith("preset_create"):
            return "PARSE_FAIL", msg, secs, "0"
        return "CHAIN_FAIL", msg, secs, "0"
    if pr == "0" or msg.startswith("mtl_filter_chain_frame"):
        # "command buffer did not complete" is a GPU fault/hang the driver gave up on.
        return "FRAME_FAIL", msg or "present failed", secs, "0"
    cls = "APPLIED" if int(diff) else "IDENTICAL"
    return cls, f"{w}x{h} diffpx={diff} black={black}% mean={mean}", secs, "0"


def params(a, preset):
    cmd = [sys.executable, os.path.abspath(__file__), "--params-one", a.core, preset]
    rc, out, err, secs = run(cmd, a.libdir, a.timeout)
    if rc is None:
        return "TIMEOUT", "", secs
    if rc != 0:
        return "CRASH", crash_detail(rc, err, out), secs
    m = re.search(r"PARAMS support=(\d) count=(\d+) list=(\d+) first=(.*)", out)
    if not m:
        return "CRASH", "no PARAMS line: " + out.strip()[-200:], secs
    support, count, listed, first = m.groups()
    if count != listed:
        return "MISMATCH", f"count={count} list={listed}", secs
    # count=0 is also how the export reports a preset it could not parse.
    return ("OK" if count != "0" else "ZERO"), f"count={count} first={first}", secs


def select(a):
    allp = sorted(os.path.relpath(p, a.shaders) for p in glob.glob(os.path.join(a.shaders, "**", "*.slangp"), recursive=True))
    pats = a.glob or ["*"]
    chosen = [p for p in allp if any(fnmatch.fnmatch(p, g) for g in pats)]
    if a.sample and a.sample < len(chosen):
        step = len(chosen) / a.sample
        chosen = [chosen[int(i * step)] for i in range(a.sample)]
    for rel in a.preset or []:
        if not os.path.isfile(os.path.join(a.shaders, rel)):
            sys.exit(f"error: --preset {rel} is not a file under {a.shaders}")
        if rel not in chosen:
            chosen.append(rel)
    return chosen


def sha256(path):
    with open(path, "rb") as fh:
        return hashlib.sha256(fh.read()).hexdigest()


def preflight(a):
    lib = os.path.join(a.libdir, "librashader.dylib")
    for path, hint in ((HARNESS, "make shader-sweep-tool"), (a.core, "make core"),
                       (lib, "scripts/fetch_librashader_macos.sh")):
        if not os.path.isfile(path):
            sys.exit(f"error: {path} is missing (run {hint})")
    if a.frame != "builtin" and not os.path.isfile(a.frame):
        sys.exit(f"error: --frame {a.frame} does not exist")
    # One frame and a fixed timeout: the control checks the comparison, not the GPU budget.
    cls, detail, _, _ = render(a, "none", None, nframes=1, timeout=60)
    if cls != "IDENTICAL":
        print(f"error: control run (no shader) is {cls}: {detail} - the comparison is broken", file=sys.stderr)
        sys.exit(2)
    if not a.no_params:
        rc, out, err, _ = run([sys.executable, os.path.abspath(__file__), "--params-one", a.core, "/nonexistent.slangp"],
                              a.libdir, a.timeout)
        if rc != 0 or "support=1" not in out:
            print(f"error: CheckShaderSupport is false from {a.libdir}: {(out + err).strip()[-300:]}", file=sys.stderr)
            sys.exit(2)
    return sha256(lib)


def main():
    if len(sys.argv) == 4 and sys.argv[1] == "--params-one":
        params_one(sys.argv[2], sys.argv[3])
        return 0
    ap = argparse.ArgumentParser(description="Manual macOS Metal shader sweep (docs/shader-sweep.md).")
    ap.add_argument("shaders", help="slang-shaders checkout (the folder holding crt/, bezel/, ...)")
    ap.add_argument("--out", default=os.path.join(ROOT, "runs", "shader-sweep-" + time.strftime("%Y%m%d-%H%M%S")))
    ap.add_argument("--jobs", type=int, default=4, help="presets in flight at once (default 4)")
    ap.add_argument("--timeout", type=int, default=60, help="seconds per process (default 60)")
    ap.add_argument("--nframes", type=int, default=60, help="frames presented per preset (default 60)")
    ap.add_argument("--scale", type=int, default=4, help="drawable = frame size x scale (default 4)")
    ap.add_argument("--frame", default="builtin", help="reference frame PNG (default: built-in pattern)")
    ap.add_argument("--glob", action="append", help="fnmatch on the relative path, repeatable (default all)")
    ap.add_argument("--sample", type=int, default=0, help="keep N evenly spaced presets after --glob")
    ap.add_argument("--preset", action="append", help="relative path always included, repeatable")
    ap.add_argument("--keep-images", action="store_true", help="write each presented frame to <out>/img/")
    ap.add_argument("--no-params", action="store_true", help="skip the GetShaderParams pass")
    ap.add_argument("--no-caffeinate", action="store_true", help="do not hold a caffeinate assertion")
    ap.add_argument("--no-recheck", action="store_true", help="do not re-run FRAME_FAIL presets one at a time")
    ap.add_argument("--core", default=os.path.join(ROOT, "InteropDLL", "obj.osx-arm64", "MesenCore.dylib"))
    ap.add_argument("--libdir", default=os.path.join(ROOT, "UI", "Dependencies"), help="folder with librashader.dylib")
    a = ap.parse_args()
    a.shaders, a.core, a.libdir = (os.path.abspath(os.path.expanduser(x)) for x in (a.shaders, a.core, a.libdir))
    if a.frame != "builtin":
        a.frame = os.path.abspath(a.frame)
    if not os.path.isdir(a.shaders):
        sys.exit(f"error: {a.shaders} is not a directory")
    if not a.no_caffeinate and os.path.exists("/usr/bin/caffeinate"):
        subprocess.Popen(["/usr/bin/caffeinate", "-ims", "-w", str(os.getpid())])
    libsha = preflight(a)
    presets = select(a)
    os.makedirs(os.path.join(a.out, "img") if a.keep_images else a.out, exist_ok=True)
    print(f"{len(presets)} presets, {a.jobs} jobs, {a.nframes} frames, timeout {a.timeout}s -> {a.out}", flush=True)

    def one(rel):
        p = os.path.join(a.shaders, rel)
        img = os.path.join(a.out, "img", rel.replace("/", "__") + ".png") if a.keep_images else None
        row = render(a, p, img)
        prow = ("-", "", 0.0) if a.no_params else params(a, p)
        if row[0] in FAILING or prow[0] in FAILING:
            print(f"!! {rel}: render {row[0]} {row[1]} / params {prow[0]} {prow[1]}", flush=True)
        return (rel,) + row + prow

    rows = []
    with ThreadPoolExecutor(max(a.jobs, 1)) as ex:
        for n, r in enumerate(ex.map(one, presets), 1):
            rows.append(r)
            if n % 50 == 0:
                print(f"done {n}/{len(presets)}", flush=True)
    # A GPU fault can depend on what else the GPU is doing: re-run each
    # FRAME_FAIL alone, and call the ones that then render FRAME_FLAKY.
    for i, r in enumerate(rows):
        if r[1] == "FRAME_FAIL" and not a.no_recheck:
            again = render(a, os.path.join(a.shaders, r[0]), None)
            if again[0] != "FRAME_FAIL":
                rows[i] = (r[0], "FRAME_FLAKY", f"parallel: {r[2]}; alone: {again[0]} {again[1]}") + r[3:]
    with open(os.path.join(a.out, "sweep.tsv"), "w") as fh:
        fh.write("preset\tclass\tdetail\tsecs\trc\tpclass\tpdetail\tpsecs\n")
        for rel, c, d, s, rc, pc, pd, ps in rows:
            cells = [rel, c, d, f"{s:.1f}", rc, pc, pd, f"{ps:.1f}"]
            fh.write("\t".join(x.replace("\t", " ").replace("\n", " ") for x in cells) + "\n")
    rc_count = Counter(r[1] for r in rows)
    pc_count = Counter(r[5] for r in rows)
    bad = [r for r in rows if r[1] in FAILING or r[5] in FAILING]
    lines = [f"shaders  {a.shaders}", f"presets  {len(rows)}  (glob={a.glob or ['*']} sample={a.sample})",
             f"frame    {a.frame}  scale={a.scale}  nframes={a.nframes}  timeout={a.timeout}s  jobs={a.jobs}",
             f"librashader.dylib sha256 {libsha}", f"core     {a.core}",
             "render   " + "  ".join(f"{k}={v}" for k, v in sorted(rc_count.items())),
             "params   " + "  ".join(f"{k}={v}" for k, v in sorted(pc_count.items()))]
    lines += [f"FAIL     {r[0]}: render {r[1]} / params {r[5]}" for r in bad]
    with open(os.path.join(a.out, "summary.txt"), "w") as fh:
        fh.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
