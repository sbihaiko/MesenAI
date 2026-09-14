#!/usr/bin/env python3
"""Tests for scripts/fetch_pack.py -- the allow-listed community-pack
downloader (ADR-0138 §41: per-hop validation). stdlib only, no network
beyond 127.0.0.1.

What is covered, and how the loopback constraint is handled:

* `validate_url_shape` / `match_host` -- pure helpers: https only, hostname
  (not netloc) matching, userinfo and non-443 ports refused, case-insensitive
  host, `path_contains_any` honoured, suffix (`host_ends_with`) entries.
* `extract_drive_id` -- the `?id=` value is held to the same charset as the
  `/d/<id>` form.
* Dropbox and MEGA (ADR-0187) -- `force_dropbox_download`'s dl=1 rewrite,
  `parse_mega_url`'s key derivation pinned against a known vector, `scrub`
  proving a MEGA fragment never survives into a message, the MEGA API
  request shape (ssl=1), and the new allow-list entries resolving to their
  kinds. The MEGA API call is exercised against a stubbed `open_validated`;
  no network.
* `_NoRedirect` -- against a real `http.server` on 127.0.0.1 that answers
  302: the fetcher's opener surfaces the 3xx as an HTTPError while urllib's
  default opener follows it, proving the manual hop loop is the only path a
  redirect can take.
* `open_validated` -- the real hop loop, driven end to end against the same
  loopback server. The fetcher refuses plain http and non-public IPs by
  design, so the test injects an opener that rewrites the validated https
  URL onto the loopback server (`https://github.com/...` -> the local port)
  and stubs `assert_public_host` (DNS is not under test here). Asserts: a
  302 to an off-list host is refused naming that host; a 302 to another
  allow-listed host is followed and the body returned; a self-redirect trips
  the MAX_REDIRECTS cap; a 3xx without Location is an error.

Usage: python3 scripts/test_fetch_pack.py
"""
from __future__ import annotations

import base64
import http.server
import io
import json
import struct
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import fetch_pack  # noqa: E402

FAILURES: list[str] = []
HOSTS = fetch_pack.load_allowlist(str(SCRIPTS / "pack_host_allowlist.json"))


def fail(msg: str) -> None:
    FAILURES.append(msg)
    print(f"FAIL: {msg}")


def ok(msg: str) -> None:
    print(f"PASS: {msg}")


# ---------------------------------------------------------------------------
# Loopback server: every path decides its own response.
# ---------------------------------------------------------------------------

BODY = b"PK\x05\x06" + b"\x00" * 18  # an empty zip: what a pack fetch returns


class _Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_args):  # keep the test output quiet
        pass

    def do_GET(self):  # noqa: N802 - http.server API
        path = urllib.parse.urlparse(self.path).path
        if path == "/x/releases/off-list":
            self._redirect("https://evil.example/pack.zip")
        elif path == "/x/releases/on-list":
            self._redirect("https://raw.githubusercontent.com/x/y/pack.zip")
        elif path == "/x/releases/loop":
            self._redirect("https://github.com/x/releases/loop")
        elif path == "/x/releases/no-location":
            self.send_response(302)
            self.end_headers()
        elif path == "/x/releases/userinfo":
            self._redirect("https://github.com@evil.example/x/releases/pack.zip")
        elif path == "/x/releases/port":
            self._redirect("https://github.com:8443/x/releases/pack.zip")
        elif path == "/x/y/pack.zip":
            self.send_response(200)
            self.send_header("Content-Type", "application/zip")
            self.send_header("Content-Length", str(len(BODY)))
            self.end_headers()
            self.wfile.write(BODY)
        else:
            self.send_response(404)
            self.end_headers()

    def _redirect(self, location: str) -> None:
        self.send_response(302)
        self.send_header("Location", location)
        self.end_headers()


class _Loopback:
    def __enter__(self):
        self.server = http.server.HTTPServer(("127.0.0.1", 0), _Handler)
        self.port = self.server.server_address[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        return self

    def __exit__(self, *exc):
        self.server.shutdown()
        self.server.server_close()
        return False

    def url(self, path: str) -> str:
        return f"http://127.0.0.1:{self.port}{path}"


class _LoopbackOpener:
    """Stands in for fetch_pack._OPENER: takes the *validated* https URL the
    hop loop produced and issues the request to the loopback server instead
    (same path, same 3xx/200 semantics), never following redirects itself."""

    def __init__(self, loopback: _Loopback):
        self.loopback = loopback
        self.inner = urllib.request.build_opener(fetch_pack._NoRedirect())
        self.seen: list[str] = []

    def open(self, req, timeout=None):
        self.seen.append(req.full_url)
        parsed = urllib.parse.urlparse(req.full_url)
        if parsed.scheme != "https":
            raise AssertionError(f"hop loop let a non-https URL through: {req.full_url}")
        local = urllib.request.Request(  # noqa: S310 - 127.0.0.1 test server
            self.loopback.url(parsed.path), headers=dict(req.header_items()))
        return self.inner.open(local, timeout=timeout)


# ---------------------------------------------------------------------------
# Pure helpers
# ---------------------------------------------------------------------------

def check_url_shape_and_match_host():
    good = "https://github.com/user/repo/releases/download/v1/pack.zip"
    if fetch_pack.match_host(good, HOSTS) is None:
        fail("github.com releases URL must match the allow-list")
        return
    if fetch_pack.match_host("https://GitHub.COM/user/repo/releases/download/v1/pack.zip", HOSTS) is None:
        fail("hostname match must be case-insensitive")
        return
    rejected = {
        "http://github.com/user/repo/releases/download/v1/pack.zip": "plain http",
        "https://github.com@evil.example/user/repo/releases/x.zip": "userinfo (netloc match would see github.com)",
        "https://user:pw@github.com/user/repo/releases/x.zip": "userinfo with password",
        "https://github.com:8443/user/repo/releases/x.zip": "non-443 port",
        "https://github.com/user/repo/blob/main/pack.zip": "github.com path outside /releases/ /archive/",
        "https://evil.example/releases/pack.zip": "host not listed",
        "https://notmediafire.com/file/x": "suffix entry must not match a bare look-alike",
        "https://mediafire.com.evil.example/file/x": "suffix entry must not match a superstring",
        "https:///releases/x.zip": "empty hostname",
    }
    for url, why in rejected.items():
        if fetch_pack.match_host(url, HOSTS) is not None:
            fail(f"match_host accepted {url} ({why})")
            return
    if fetch_pack.match_host("https://download1234.mediafire.com/abc/pack.zip", HOSTS) is None:
        fail("downloadN.mediafire.com must match the host_ends_with entry")
        return
    if fetch_pack.match_host("https://github.com:443/user/repo/releases/download/v1/pack.zip", HOSTS) is None:
        fail("an explicit :443 is still port 443")
        return
    for url, needle in (
        ("http://github.com/x/releases/y", "https"),
        ("https://a@github.com/x/releases/y", "userinfo"),
        ("https://github.com:80/x/releases/y", "port"),
        ("https://github.com:abc/x/releases/y", "invalid port"),
    ):
        try:
            fetch_pack.validate_url_shape(url)
        except ValueError as exc:
            if needle not in str(exc):
                fail(f"validate_url_shape({url}) error does not name the reason ({needle}): {exc}")
                return
        else:
            fail(f"validate_url_shape accepted {url}")
            return
    ok("validate_url_shape/match_host: https, hostname-only match, userinfo/port/path/suffix rules")


def check_drive_id():
    if fetch_pack.extract_drive_id("https://drive.google.com/file/d/1AbC_-9/view") != "1AbC_-9":
        fail("/d/<id> form not extracted")
        return
    if fetch_pack.extract_drive_id("https://drive.google.com/open?id=1AbC_-9") != "1AbC_-9":
        fail("?id= form not extracted")
        return
    for bad in ("https://drive.google.com/open?id=1AbC/../x", "https://drive.google.com/open?id=a%20b",
                "https://drive.google.com/open?id=", "https://drive.google.com/open?x=1"):
        try:
            fetch_pack.extract_drive_id(bad)
        except ValueError:
            continue
        fail(f"extract_drive_id accepted {bad}")
        return
    ok("extract_drive_id: ?id= value held to ^[a-zA-Z0-9_-]+$ like the /d/ form")


# ---------------------------------------------------------------------------
# Loopback: the redirect handler and the hop loop
# ---------------------------------------------------------------------------

def check_no_redirect_handler(lb: _Loopback):
    default = urllib.request.build_opener()
    # urllib's default opener follows a 302 (here to an https host it cannot
    # reach, so the follow shows up as a URLError, not as the 302).
    try:
        default.open(lb.url("/x/releases/loop"), timeout=5)
    except urllib.error.HTTPError as exc:
        if exc.code == 302:
            fail("default opener did not follow the redirect — the comparison is meaningless")
            return
    except Exception:  # noqa: BLE001, S110 - followed to https://github.com/... and failed to connect: as expected
        pass
    strict = urllib.request.build_opener(fetch_pack._NoRedirect())
    try:
        strict.open(lb.url("/x/releases/loop"), timeout=5)
    except urllib.error.HTTPError as exc:
        if exc.code != 302 or exc.headers.get("Location") != "https://github.com/x/releases/loop":
            fail(f"_NoRedirect surfaced the wrong error: {exc.code} {dict(exc.headers)}")
            return
    else:
        fail("_NoRedirect opener followed the 302 instead of raising HTTPError")
        return
    ok("_NoRedirect: 302 surfaces as HTTPError (default opener would have followed it)")


def _run_hop_loop(lb: _Loopback, path: str):
    opener = _LoopbackOpener(lb)
    original = fetch_pack.assert_public_host
    fetch_pack.assert_public_host = lambda host: None  # DNS is not under test on loopback
    try:
        return fetch_pack.open_validated(f"https://github.com{path}", HOSTS, opener=opener), opener
    finally:
        fetch_pack.assert_public_host = original


def check_redirect_to_off_list_host_refused(lb: _Loopback):
    try:
        _run_hop_loop(lb, "/x/releases/off-list")
    except ValueError as exc:
        if "not allow-listed" not in str(exc) or "evil.example" not in str(exc):
            fail(f"off-list redirect refused with the wrong message: {exc}")
            return
    else:
        fail("redirect to an off-list host was followed")
        return
    ok("302 to an off-list host is refused, naming the host")


def check_redirect_with_userinfo_or_port_refused(lb: _Loopback):
    for path, needle in (("/x/releases/userinfo", "userinfo"), ("/x/releases/port", "port")):
        try:
            _run_hop_loop(lb, path)
        except ValueError as exc:
            if needle not in str(exc):
                fail(f"redirect to {path} target refused with the wrong message: {exc}")
                return
        else:
            fail(f"redirect target with {needle} was followed")
            return
    ok("302 to an allow-listed host with userinfo or a non-443 port is refused")


def check_redirect_to_on_list_host_followed(lb: _Loopback):
    try:
        (resp, entry), opener = _run_hop_loop(lb, "/x/releases/on-list")
    except Exception as exc:  # noqa: BLE001 - reported as a failure
        fail(f"redirect to an allow-listed host was not followed: {exc}")
        return
    body = resp.read()
    if body != BODY:
        fail(f"followed redirect returned the wrong body: {body!r}")
        return
    if entry.get("host") != "raw.githubusercontent.com":
        fail(f"entry returned is not the final hop's: {entry}")
        return
    if opener.seen != ["https://github.com/x/releases/on-list", "https://raw.githubusercontent.com/x/y/pack.zip"]:
        fail(f"unexpected hop sequence: {opener.seen}")
        return
    ok("302 to another allow-listed host is followed, one request per validated hop")


def check_redirect_loop_capped(lb: _Loopback):
    try:
        (_resp, _entry), _opener = _run_hop_loop(lb, "/x/releases/loop")
    except ValueError as exc:
        if "too many redirects" not in str(exc):
            fail(f"redirect loop failed with the wrong message: {exc}")
            return
    else:
        fail("redirect loop was not capped")
        return
    try:
        _run_hop_loop(lb, "/x/releases/no-location")
    except ValueError as exc:
        if "no Location" not in str(exc):
            fail(f"302 without Location failed with the wrong message: {exc}")
            return
    else:
        fail("302 without Location was accepted")
        return
    ok(f"redirect loop stops after MAX_REDIRECTS={fetch_pack.MAX_REDIRECTS}; 3xx without Location is an error")


def check_dropbox_dl_rewrite():
    """dl=1 replaces any dl the link carried, and rlkey survives -- the
    /scl/fi/ links 404 without it (ADR-0187 section 2)."""
    cases = [
        ("https://www.dropbox.com/s/abc/Pack.zip?dl=0", "dl=1", []),
        ("https://www.dropbox.com/s/abc/Pack.zip", "dl=1", []),
        ("https://www.dropbox.com/scl/fi/x/P.zip?rlkey=k1&dl=0", "dl=1", ["rlkey=k1"]),
        ("https://www.dropbox.com/scl/fi/x/P.zip?dl=1&st=s", "dl=1", ["st=s"]),
    ]
    for url, expected, must_keep in cases:
        out = fetch_pack.force_dropbox_download(url)
        query = urllib.parse.urlparse(out).query
        pairs = query.split("&")
        if pairs.count(expected) != 1 or any(p.startswith("dl=") and p != expected for p in pairs):
            fail(f"force_dropbox_download({url}) gave {out!r}, expected exactly one {expected}")
            return
        for keep in must_keep:
            if keep not in pairs:
                fail(f"force_dropbox_download({url}) dropped {keep!r}: {out!r}")
                return
    ok("force_dropbox_download: dl=1 replaces any dl value and other params (rlkey) survive")


def check_mega_url_parsing():
    """The key derivation is pinned against a vector: a bug here yields
    plausible garbage rather than an error (ADR-0187 Consequences)."""
    key = bytes(range(32))
    fragment = base64.urlsafe_b64encode(key).rstrip(b"=").decode()
    handle, aes_key, nonce = fetch_pack.parse_mega_url(f"https://mega.nz/file/AbCd1234#{fragment}")
    if handle != "AbCd1234":
        fail(f"parse_mega_url returned handle {handle!r}")
        return
    words = struct.unpack(">8I", key)
    want_key = struct.pack(">4I", *(words[i] ^ words[i + 4] for i in range(4)))
    if aes_key != want_key:
        fail(f"parse_mega_url derived AES key {aes_key.hex()}, expected {want_key.hex()}")
        return
    if nonce != struct.pack(">2I", words[4], words[5]) + b"\0" * 8:
        fail(f"parse_mega_url derived nonce {nonce.hex()}")
        return
    # A folder-link fragment is "<key>!<handle>!<key>"; only the leading key is ours.
    _h, aes2, _n = fetch_pack.parse_mega_url(f"https://mega.nz/file/AbCd1234#{fragment}!x!y")
    if aes2 != want_key:
        fail("parse_mega_url did not stop at the '!' in a folder-style fragment")
        return
    for bad, why in [
        ("https://mega.nz/file/AbCd1234", "no fragment"),
        ("https://mega.nz/file/AbCd1234#short", "wrong key length"),
        ("https://mega.nz/notafile#" + fragment, "no handle"),
    ]:
        try:
            fetch_pack.parse_mega_url(bad)
        except ValueError:
            continue
        fail(f"parse_mega_url accepted a URL with {why}: {bad}")
        return
    ok("parse_mega_url: key/nonce derivation pinned; missing or malformed fragments rejected")


def check_mega_key_is_never_printed():
    """A MEGA fragment is a decryption key; scrub() runs on whole messages
    because urllib embeds URLs in its own error strings (ADR-0187 section 3)."""
    secret = "zVnPz7NURkiwlODHLRtd3C1Oe1nhcwg7C_6vXbL8kyM"
    text = f"<urlopen error timed out> for https://mega.nz/file/yVsRmCiS#{secret} (retrying)"
    scrubbed = fetch_pack.scrub(text)
    if secret in scrubbed:
        fail(f"scrub() left the key in: {scrubbed}")
        return
    if "mega.nz/file/yVsRmCiS" not in scrubbed:
        fail(f"scrub() removed the handle too, losing the diagnostic: {scrubbed}")
        return
    unrelated = "download failed: host not allow-listed: example.com"
    if fetch_pack.scrub(unrelated) != unrelated:
        fail("scrub() altered a message with no MEGA link in it")
        return
    ok("scrub: a MEGA key never survives into a message, the handle does")


def check_mega_api_request_shape():
    """ssl=1 is what keeps the API from answering with an http:// URL that
    validate_url_shape would (correctly) refuse."""
    captured = {}

    def fake_open_validated(url, hosts, data=None, content_type=None, **kwargs):
        captured["url"] = url
        captured["data"] = data
        captured["content_type"] = content_type
        return io.BytesIO(json.dumps([{"s": 123, "g": "https://gfs1.userstorage.mega.co.nz/dl/x"}]).encode()), {}

    real = fetch_pack.open_validated
    fetch_pack.open_validated = fake_open_validated
    try:
        url, size = fetch_pack.request_mega_download_url("AbCd1234", [])
    finally:
        fetch_pack.open_validated = real
    body = json.loads(captured["data"])
    if body != [{"a": "g", "g": 1, "ssl": 1, "p": "AbCd1234"}]:
        fail(f"MEGA API body was {body}")
        return
    if captured["content_type"] != "application/json":
        fail(f"MEGA API request content type was {captured['content_type']!r}")
        return
    if url != "https://gfs1.userstorage.mega.co.nz/dl/x" or size != 123:
        fail(f"request_mega_download_url returned {url!r}, {size!r}")
        return

    def erroring_open(url, hosts, data=None, content_type=None, **kwargs):
        return io.BytesIO(b"-9"), {}

    fetch_pack.open_validated = erroring_open
    try:
        fetch_pack.request_mega_download_url("AbCd1234", [])
    except ValueError as exc:
        if "MEGA API" not in str(exc):
            fail(f"MEGA API error surfaced as: {exc}")
            return
    else:
        fail("a MEGA API error code was not surfaced")
        return
    finally:
        fetch_pack.open_validated = real
    ok("request_mega_download_url: POSTs ssl=1, reads 'g', surfaces API error codes")


def check_new_hosts_are_allow_listed():
    hosts = fetch_pack.load_allowlist(str(SCRIPTS / "pack_host_allowlist.json"))
    expected = {
        "https://www.dropbox.com/s/abc/Pack.zip?dl=0": "dropbox",
        "https://www.dropbox.com/scl/fi/x/P.zip?rlkey=k": "dropbox",
        "https://uc1.dl.dropboxusercontent.com/cd/0/get/x/file": "direct",
        "https://mega.nz/file/AbCd1234#key": "mega",
        "https://g.api.mega.co.nz/cs?id=0": "direct",
        "https://gfs302n298.userstorage.mega.co.nz/dl/x": "direct",
    }
    for url, kind in expected.items():
        entry = fetch_pack.match_host(url, hosts)
        if entry is None or entry["kind"] != kind:
            fail(f"{url} matched {entry and entry['kind']!r}, expected {kind!r}")
            return
    # A dropbox host outside the share-path shapes stays off the list.
    if fetch_pack.match_host("https://www.dropbox.com/home", hosts) is not None:
        fail("a non-share dropbox.com path was allow-listed")
        return
    ok("allow-list: dropbox/mega share URLs and their CDN hops resolve to the right kind")


def main() -> int:
    check_url_shape_and_match_host()
    check_drive_id()
    check_dropbox_dl_rewrite()
    check_mega_url_parsing()
    check_mega_key_is_never_printed()
    check_mega_api_request_shape()
    check_new_hosts_are_allow_listed()
    with _Loopback() as lb:
        check_no_redirect_handler(lb)
        check_redirect_to_off_list_host_refused(lb)
        check_redirect_with_userinfo_or_port_refused(lb)
        check_redirect_to_on_list_host_followed(lb)
        check_redirect_loop_capped(lb)
    if FAILURES:
        print(f"\n{len(FAILURES)} failure(s)")
        return 1
    print("\nall fetch_pack checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
