"""Headless suite for the painting-surface name contract (`asset_names.py`).

F12.4 / ADR-0213. What is tested here is the three readers the contract serves:
Photoshop's *Generate Image Assets* layer-name grammar, a file system that may
be Windows, and the kit's own manifest. Every rule that refuses a name is here
with the reader it protects, and every rule that *accepts* one is here too —
the names the four generators actually compose must keep passing, or this
contract would be a guard against nothing.

Run:  python3 scripts/test_asset_names.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asset_names as A  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


def _refuses(name, needle):
    reasons = " ".join(A.check_asset_name(name))
    return bool(reasons) and needle in reasons


def test_the_names_the_generators_compose_are_all_valid():
    # These are the real surfaces of a Contra kit, read off
    # runs/f918v-20260915/contra/kit. If the contract ever refuses one of
    # these it is the contract that is wrong, not the kit.
    live = [
        "usr000.png", "usr006.png",
        "Chr_0.png", "Chr_10.png", "Chr_31.png",
        "Chr_0.orig.png", "Chr_0.legend.png",
        "screen001.png", "screen004.png",
        "stage1-000.png", "pano-stage1-000.png",
        "stage3-boss-to-stage4-base.chain-000.png",
    ]
    for name in live:
        check(A.is_asset_name(name), f"live kit name is valid: {name}",
              "; ".join(A.check_asset_name(name)))


def test_a_comma_is_refused_because_photoshop_splits_on_it():
    check(_refuses("run,walk.png", "comma"),
          "a comma is refused (Photoshop reads it as two assets)")


def test_a_path_separator_is_refused_in_both_directions():
    check(_refuses("sheets/usr000.png", "path separator"),
          "a forward slash is refused (Photoshop reads it as a subfolder)")
    check(_refuses("sheets\\usr000.png", "path separator"),
          "a backslash is refused")


def test_a_scale_prefix_is_refused_in_all_three_forms():
    check(_refuses("2x usr000.png", "scale directive"),
          "`2x ` is refused (Photoshop would resize and shorten the name)")
    check(_refuses("200% usr000.png", "scale directive"),
          "`200% ` is refused")
    check(_refuses("100x50 usr000.png", "scale directive"),
          "`100x50 ` is refused")
    # The same characters without the space are not a directive, and a real
    # page name uses them.
    check(A.is_asset_name("2x-zoom.png"),
          "`2x-zoom.png` is accepted: no space, so it is not a directive")


def test_a_bit_depth_suffix_is_refused_because_it_drops_alpha():
    check(_refuses("usr000.png24", "does not end in `.png`"),
          "`.png24` is refused (Photoshop would write 24-bit, without alpha)")
    check(_refuses("usr000.png8", "does not end in `.png`"),
          "`.png8` is refused")
    check(_refuses("usr000.PNG", "does not end in `.png`"),
          "`.PNG` is refused: a different file on a case-sensitive host")


def test_windows_illegal_characters_and_device_names_are_refused():
    for ch in '<>:"|?*':
        check(_refuses(f"usr{ch}000.png", "Windows refuses"),
              f"{ch!r} is refused (Windows)")
    check(_refuses("aux.png", "reserved device name"),
          "`aux.png` is refused: reserved on Windows whatever the extension")
    check(_refuses("COM1.png", "reserved device name"),
          "`COM1.png` is refused")
    check(A.is_asset_name("auxiliary.png"),
          "`auxiliary.png` is accepted: the reserved rule matches the whole "
          "stem, not a prefix")


def test_whitespace_and_trailing_dots_are_refused():
    check(_refuses(" usr000.png", "whitespace"), "a leading space is refused")
    check(_refuses("usr000.png ", "whitespace"), "a trailing space is refused")
    check(_refuses("usr000 .png", "space or a dot"),
          "a stem ending in a space is refused (Windows strips it silently)")
    check(_refuses("usr000..png", "space or a dot"),
          "a stem ending in a dot is refused")


def test_non_ascii_and_control_characters_are_refused():
    check(_refuses("cabeça.png", "not ASCII"),
          "a non-ASCII name is refused: a kit travels through zips")
    check(_refuses("usr\t000.png", "control character"),
          "a tab is refused")


def test_an_over_long_name_is_refused_with_its_length():
    name = "a" * 120 + ".png"
    check(_refuses(name, "124 characters"),
          "an over-long name is refused and the reason states its length")
    check(A.is_asset_name("a" * (A.MAX_NAME - 4) + ".png"),
          "a name exactly at the limit is accepted")


def test_an_empty_or_extensionless_name_is_refused():
    check(_refuses("", "empty") or A.check_asset_name("") == ["the name is empty"],
          "an empty name is refused")
    check(_refuses(".png", "no stem"), "a bare extension is refused")
    check(_refuses("usr000", "does not end in `.png`"),
          "a name with no extension is refused: Photoshop generates nothing "
          "for a layer it does not recognise")


def test_a_case_only_collision_is_caught_across_the_folder_not_the_name():
    both = ["Chr_0.png", "chr_0.png"]
    for name in both:
        check(A.is_asset_name(name), f"{name} is valid on its own")
    reasons = A.check_asset_set(both)
    check(len(reasons) == 1 and "differ only in case" in reasons[0],
          "the pair is refused as a set: macOS and Windows would keep one file",
          str(reasons))
    check(A.check_asset_set(["Chr_0.png", "Chr_1.png"]) == [],
          "distinct names in one folder pass the set rule")
    check(A.check_asset_set(["Chr_0.png", "Chr_0.png"]) == [],
          "the same name twice is not a case collision: a folder cannot hold "
          "it twice anyway, and reporting it here would be noise")


def test_require_raises_for_a_name_we_composed_rather_than_renaming_it():
    A.require_asset_name("usr000.png", where="test")
    try:
        A.require_asset_name("run,walk.png", where="artist_kit.py")
        check(False, "require_asset_name raises on a bad name", "it returned")
    except A.AssetNameError as exc:
        check("artist_kit.py" in str(exc) and "comma" in str(exc),
              "require_asset_name raises, naming the caller and the reason",
              str(exc))


def test_sanitize_turns_outside_input_into_a_name_that_passes():
    cases = [
        ("stage 1, the base", "stage-1-the-base"),
        ("Contra (U) [!]", "Contra-U"),
        ("  ..leading", "leading"),
        ("cabeça", "cabe-a"),
        ("aux", "aux-page"),
        ("", "surface"),
    ]
    for raw, want in cases:
        got = A.sanitize_asset_stem(raw)
        check(got == want, f"sanitize({raw!r}) == {want!r}", f"got {got!r}")
        check(A.is_asset_name(got + A.SURFACE_EXT),
              f"sanitize({raw!r}) produces a valid surface name",
              "; ".join(A.check_asset_name(got + A.SURFACE_EXT)))


def test_sanitize_leaves_a_name_that_is_already_safe_untouched():
    for raw in ["stage1-000", "pano-stage1-000", "Chr_0", "usr000"]:
        check(A.sanitize_asset_stem(raw) == raw,
              f"sanitize leaves {raw!r} alone: a safe stem is not rewritten")


def test_sanitize_truncates_without_leaving_a_trailing_separator():
    got = A.sanitize_asset_stem("x" * 200)
    check(len(got + A.SURFACE_EXT) <= A.MAX_NAME,
          "a very long stem is truncated to fit the limit", str(len(got)))
    check(A.is_asset_name(got + A.SURFACE_EXT),
          "the truncated stem still produces a valid name")
    got = A.sanitize_asset_stem("y" * (A.MAX_NAME - 6) + " - tail")
    check(not got.endswith("-"),
          "truncation does not leave a trailing separator", repr(got))


def test_the_layer_name_an_artist_pastes_is_the_base_name_only():
    check(A.asset_name_for("sheets/usr000.png") == "usr000.png",
          "a kit path yields the base name: Photoshop reads `/` as a subfolder "
          "under its own -assets folder")
    check(A.asset_name_for("usr000.png") == "usr000.png",
          "a bare name is returned unchanged")


def main():
    tests = [
        test_the_names_the_generators_compose_are_all_valid,
        test_a_comma_is_refused_because_photoshop_splits_on_it,
        test_a_path_separator_is_refused_in_both_directions,
        test_a_scale_prefix_is_refused_in_all_three_forms,
        test_a_bit_depth_suffix_is_refused_because_it_drops_alpha,
        test_windows_illegal_characters_and_device_names_are_refused,
        test_whitespace_and_trailing_dots_are_refused,
        test_non_ascii_and_control_characters_are_refused,
        test_an_over_long_name_is_refused_with_its_length,
        test_an_empty_or_extensionless_name_is_refused,
        test_a_case_only_collision_is_caught_across_the_folder_not_the_name,
        test_require_raises_for_a_name_we_composed_rather_than_renaming_it,
        test_sanitize_turns_outside_input_into_a_name_that_passes,
        test_sanitize_leaves_a_name_that_is_already_safe_untouched,
        test_sanitize_truncates_without_leaving_a_trailing_separator,
        test_the_layer_name_an_artist_pastes_is_the_base_name_only,
    ]
    for t in tests:
        t()
    print(f"\n{len(tests) - len(_FAILURES)}/{len(tests)} cases passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
