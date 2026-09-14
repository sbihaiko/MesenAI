#!/usr/bin/env python3
"""Downloads a community pack from an allow-listed host, for
community-pack-validate.yml. Two things make this more than a plain `curl`:

1. The allow-list check runs on every hop, not just the URL the issue
   submitter typed. `requests`/`curl -L` -- and urllib's default opener --
   would happily follow a redirect from an allowed host to an arbitrary
   one; here the opener is built with a redirect handler that refuses to
   follow anything (`_NoRedirect`), so every 3xx surfaces as an HTTPError
   and `open_validated` re-validates the Location target against the same
   allow-list (scheme, hostname, no userinfo, port 443 only) and the DNS
   check below before issuing the next request. At most MAX_REDIRECTS hops.
2. Before connecting, the target host's resolved IPs are checked against
   private/loopback/link-local/reserved ranges (this also catches the
   cloud-metadata address, 169.254.169.254) and rejected. The allow-listed
   hosts are all public multi-tenant platforms (GitHub, Google Drive, MediaFire) whose
   DNS we don't control, so this is what actually stands in for "no SSRF",
   not the hostname string match by itself.

   DNS is resolved twice -- once by `assert_public_host` and again by the
   socket connect inside urllib -- so a host that flips its answer between
   the two lookups (DNS rebinding) could in principle slip past the check.
   That residual risk is accepted rather than pinned: every allow-listed
   host is a major CDN-backed platform whose records we neither control nor
   expect to be adversarial, and pinning would mean re-implementing TLS SNI
   / Host handling for no realistic gain (ADR-0138 §41).

Google Drive (`kind: "google-drive"` in the allow-list) needs a second
request: the first response for a file too large to virus-scan is an HTML
warning page, not the file -- the real bytes come from
drive.usercontent.google.com/download with a confirm token.

MediaFire (`kind: "mediafire"`) is the same shape: the /file/ share URL
is an HTML page, and the zip is on downloadN.mediafire.com (matched via
`host_ends_with: ".mediafire.com"`).

Dropbox (`kind: "dropbox"`) needs no extra request, only the right query:
a share link renders an HTML preview unless `dl=1` is set, after which the
usual redirect chain ends on `*.dl.dropboxusercontent.com` (ADR-0187 §2).

MEGA (`kind: "mega"`) is the one host whose bytes arrive encrypted. The
decryption key lives in the URL fragment and is never sent anywhere: we
split it off locally, ask the API (a POST, hence `open_validated`'s
optional `data`) for a download URL with `ssl: 1` so it comes back https,
and decrypt AES-CTR while streaming (ADR-0187 §3). No message printed by
this module ever includes a MEGA fragment.

Usage:
  python3 scripts/fetch_pack.py <url> <out-path> --max-bytes N
      [--allowlist scripts/pack_host_allowlist.json]

Exit 0 and writes <out-path> on success. Exit 1 with a message on stderr for
any rejection (disallowed host, private-IP target, over the size cap,
malformed Drive link) -- the workflow step treats any non-zero exit as a
download failure, same as a network error.
"""
import base64
import html as htmlmod
import ipaddress
import json
import re
import socket
import struct
import sys
import urllib.error
import urllib.parse
import urllib.request

DEFAULT_ALLOWLIST = "scripts/pack_host_allowlist.json"
USER_AGENT = "MesenCE-community-pack-validator/1.0"
MAX_REDIRECTS = 5  # mirrors the client's 5-hop cap (ADR-0138 §41)
DRIVE_ID = re.compile(r"^[a-zA-Z0-9_-]+$")
REDIRECT_CODES = (301, 302, 303, 307, 308)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """Refuses to follow any redirect, so a 3xx reaches `open_validated` as
    an HTTPError and the Location target gets the same allow-list + DNS
    checks as the URL the submitter typed (instead of urllib silently
    following it to an arbitrary host)."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


_OPENER = urllib.request.build_opener(_NoRedirect())


def load_allowlist(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)["hosts"]


def validate_url_shape(url):
    """Rejects, with a ValueError naming the reason, any URL that is not
    `https://<hostname>[:443]/...` -- no userinfo (`user@host` tricks the
    eye and some parsers), no non-443 port (nothing allow-listed serves
    packs elsewhere), a real hostname. Returns the lower-cased hostname."""
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https":
        raise ValueError(f"URL scheme must be https: {url}")
    if parsed.username is not None or parsed.password is not None or "@" in parsed.netloc:
        raise ValueError(f"URL must not carry userinfo: {url}")
    try:
        port = parsed.port
    except ValueError as e:
        raise ValueError(f"URL has an invalid port: {url}") from e
    if port is not None and port != 443:
        raise ValueError(f"URL port must be 443, got {port}: {url}")
    hostname = parsed.hostname
    if not hostname:
        raise ValueError(f"URL has no hostname: {url}")
    return hostname


def match_host(url, hosts):
    """The allow-list entry `url` matches, or None. Matches on the parsed
    hostname (never the raw netloc, which could hide userinfo or a port)."""
    try:
        hostname = validate_url_shape(url)
    except ValueError:
        return None
    parsed = urllib.parse.urlparse(url)
    for entry in hosts:
        host = (entry.get("host") or "").lower()
        suffix = (entry.get("host_ends_with") or "").lower()
        host_match = bool(host) and hostname == host
        suffix_match = bool(suffix) and hostname.endswith(suffix)
        if not (host_match or suffix_match):
            continue
        substrings = entry.get("path_contains_any")
        if substrings and not any(s in parsed.path for s in substrings):
            continue
        return entry
    return None


def assert_public_host(host):
    try:
        infos = socket.getaddrinfo(host, 443, proto=socket.IPPROTO_TCP)
    except socket.gaierror as e:
        raise ValueError(f"could not resolve host {host!r}: {e}") from e
    for _family, _, _, _, sockaddr in infos:
        ip = ipaddress.ip_address(sockaddr[0])
        if (
            ip.is_private
            or ip.is_loopback
            or ip.is_link_local
            or ip.is_multicast
            or ip.is_reserved
            or ip.is_unspecified
        ):
            raise ValueError(f"host {host!r} resolved to non-public address {ip}")


def open_validated(url, hosts, max_redirects=MAX_REDIRECTS, opener=None, data=None, content_type=None):
    """Follows redirects manually, re-checking the allow-list and DNS on
    every hop, instead of trusting urllib's/curl's built-in follower. The
    opener never follows a 3xx itself (`_NoRedirect`), which is what makes
    this loop the only redirect path; `opener` is injectable for tests.

    `data` makes the first request a POST (MEGA's API is POST-only); it is
    dropped on any redirect hop, because re-POSTing a body to a
    redirect target is exactly the kind of thing this loop exists to not
    do silently."""
    opener = opener or _OPENER
    headers = {"User-Agent": USER_AGENT}
    if content_type:
        headers["Content-Type"] = content_type
    for _ in range(max_redirects + 1):
        hostname = validate_url_shape(url)
        entry = match_host(url, hosts)
        if entry is None:
            raise ValueError(f"host not allow-listed: {hostname}")
        assert_public_host(hostname)
        req = urllib.request.Request(url, data=data, headers=headers)  # noqa: S310 - https + allow-list + DNS checked above
        try:
            resp = opener.open(req, timeout=30)
        except urllib.error.HTTPError as e:
            if e.code in REDIRECT_CODES:
                location = e.headers.get("Location")
                if not location:
                    raise ValueError(f"redirect ({e.code}) with no Location header") from e
                url = urllib.parse.urljoin(url, location)
                data = None
                headers.pop("Content-Type", None)
                continue
            raise
        return resp, entry
    raise ValueError(f"too many redirects (more than {max_redirects})")


MEDIAFIRE_DOWNLOAD = re.compile(
    r"""href=["'](https://download\d+\.mediafire\.com/[^"'<>]+)["']""",
    re.IGNORECASE,
)


def extract_mediafire_download_url(page_html):
    m = MEDIAFIRE_DOWNLOAD.search(page_html)
    return htmlmod.unescape(m.group(1)) if m else None


def extract_drive_id(url):
    parsed = urllib.parse.urlparse(url)
    m = re.search(r"/d/([a-zA-Z0-9_-]+)", parsed.path)
    if m:
        return m.group(1)
    qs = urllib.parse.parse_qs(parsed.query)
    if "id" in qs:
        file_id = qs["id"][0]
        if not DRIVE_ID.match(file_id):
            raise ValueError(f"Google Drive file id has unexpected characters: {file_id!r}")
        return file_id
    raise ValueError(f"could not extract a file id from Google Drive URL: {url}")


def stream_to_file(resp, out_path, max_bytes):
    declared = resp.headers.get("Content-Length")
    if declared is not None and int(declared) > max_bytes:
        raise ValueError(f"Content-Length ({declared}) exceeds the {max_bytes}-byte cap")
    written = 0
    with open(out_path, "wb") as f:
        while True:
            chunk = resp.read(1024 * 1024)
            if not chunk:
                break
            written += len(chunk)
            if written > max_bytes:
                raise ValueError(f"download exceeded the {max_bytes}-byte cap mid-transfer")
            f.write(chunk)
    return written


def fetch_direct(url, hosts, out_path, max_bytes):
    resp, _ = open_validated(url, hosts)
    return stream_to_file(resp, out_path, max_bytes)


def fetch_google_drive(url, hosts, out_path, max_bytes):
    file_id = extract_drive_id(url)
    first_url = f"https://drive.google.com/uc?export=download&id={file_id}"
    resp, _ = open_validated(first_url, hosts)
    body_preview = resp.read(1024 * 1024)
    content_type = resp.headers.get("Content-Type", "")
    if "text/html" in content_type:
        # Large-file virus-scan-warning page; the real bytes are one hop
        # away with a confirm token. Small files return the bytes directly
        # from the first request instead, so this branch is conditional.
        second_url = (
            f"https://drive.usercontent.google.com/download"
            f"?id={file_id}&export=download&confirm=t"
        )
        resp2, _ = open_validated(second_url, hosts)
        return stream_to_file(resp2, out_path, max_bytes)
    declared = resp.headers.get("Content-Length")
    if declared is not None and int(declared) > max_bytes:
        raise ValueError(f"Content-Length ({declared}) exceeds the {max_bytes}-byte cap")
    written = len(body_preview)
    if written > max_bytes:
        raise ValueError(f"download exceeded the {max_bytes}-byte cap mid-transfer")
    with open(out_path, "wb") as f:
        f.write(body_preview)
        while True:
            chunk = resp.read(1024 * 1024)
            if not chunk:
                break
            written += len(chunk)
            if written > max_bytes:
                raise ValueError(f"download exceeded the {max_bytes}-byte cap mid-transfer")
            f.write(chunk)
    return written


def fetch_mediafire(url, hosts, out_path, max_bytes):
    resp, _ = open_validated(url, hosts)
    content_type = resp.headers.get("Content-Type", "")
    if "text/html" not in content_type.lower():
        return stream_to_file(resp, out_path, max_bytes)
    page = resp.read(16 * 1024 * 1024).decode("utf-8", errors="replace")
    href = extract_mediafire_download_url(page)
    if not href:
        raise ValueError("mediafire share page has no downloadN.mediafire.com link")
    resp2, _ = open_validated(href, hosts)
    return stream_to_file(resp2, out_path, max_bytes)


def force_dropbox_download(url):
    """`dl=1` replaces whatever `dl` the share link carried (a preview link
    says `dl=0`), and every other parameter survives -- `rlkey` in
    particular, without which the newer /scl/fi/ links 404."""
    parsed = urllib.parse.urlparse(url)
    params = [(k, v) for k, v in urllib.parse.parse_qsl(parsed.query, keep_blank_values=True) if k != "dl"]
    params.append(("dl", "1"))
    return urllib.parse.urlunparse(parsed._replace(query=urllib.parse.urlencode(params)))


def fetch_dropbox(url, hosts, out_path, max_bytes):
    resp, _ = open_validated(force_dropbox_download(url), hosts)
    if "text/html" in resp.headers.get("Content-Type", "").lower():
        # A login wall or a dead share link, not a pack. Without this the
        # HTML lands on disk and gets reported as a corrupt zip instead.
        raise ValueError("dropbox link served an HTML page, not a file (deleted or access-restricted?)")
    return stream_to_file(resp, out_path, max_bytes)


MEGA_API = "https://g.api.mega.co.nz/cs?id=0"
MEGA_HANDLE = re.compile(r"^[a-zA-Z0-9_-]+$")


def _b64url_decode(value):
    value = value.replace("-", "+").replace("_", "/")
    return base64.b64decode(value + "=" * ((4 - len(value) % 4) % 4))


def parse_mega_url(url):
    """(handle, aes_key, counter_nonce) from `https://mega.nz/file/<h>#<k>`.

    `<k>` is 32 base64url bytes = eight big-endian 32-bit words. The AES key
    is the first four XORed with the last four; the CTR nonce is words 4-5
    followed by the eight zero bytes MEGA starts every file's counter at."""
    parsed = urllib.parse.urlparse(url)
    m = re.search(r"/file/([a-zA-Z0-9_-]+)", parsed.path)
    if not m:
        raise ValueError("could not extract a file handle from the MEGA URL")
    handle = m.group(1)
    if not parsed.fragment:
        raise ValueError("MEGA URL has no key fragment (the '#...' part carries the decryption key)")
    try:
        key = _b64url_decode(parsed.fragment.split("!")[0])
    except (ValueError, base64.binascii.Error) as e:
        raise ValueError("MEGA URL key fragment is not valid base64url") from e
    if len(key) != 32:
        raise ValueError(f"MEGA URL key fragment decodes to {len(key)} bytes, expected 32")
    w = struct.unpack(">8I", key)
    aes_key = struct.pack(">4I", w[0] ^ w[4], w[1] ^ w[5], w[2] ^ w[6], w[3] ^ w[7])
    nonce = struct.pack(">2I", w[4], w[5]) + b"\0" * 8
    return handle, aes_key, nonce


def request_mega_download_url(handle, hosts):
    """`ssl: 1` is not optional: without it the API answers with an http://
    URL, which validate_url_shape rejects on principle."""
    body = json.dumps([{"a": "g", "g": 1, "ssl": 1, "p": handle}]).encode()
    resp, _ = open_validated(MEGA_API, hosts, data=body, content_type="application/json")
    payload = json.loads(resp.read(64 * 1024).decode("utf-8", errors="replace"))
    if isinstance(payload, int):
        raise ValueError(f"MEGA API rejected the request (error {payload})")
    info = payload[0]
    if isinstance(info, int):
        raise ValueError(f"MEGA API has no such file (error {info})")
    if "g" not in info:
        raise ValueError("MEGA API response carried no download URL")
    return info["g"], info.get("s")


def fetch_mega(url, hosts, out_path, max_bytes):
    handle, aes_key, nonce = parse_mega_url(url)
    if not MEGA_HANDLE.match(handle):
        raise ValueError("MEGA file handle has unexpected characters")
    download_url, declared_size = request_mega_download_url(handle, hosts)
    if declared_size is not None and int(declared_size) > max_bytes:
        raise ValueError(f"MEGA declares {declared_size} bytes, over the {max_bytes}-byte cap")
    # Imported here, not at module scope, so a runner without `cryptography`
    # still downloads packs from every other host (ADR-0187 §5).
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

    decryptor = Cipher(algorithms.AES(aes_key), modes.CTR(nonce)).decryptor()
    resp, _ = open_validated(download_url, hosts)
    written = 0
    with open(out_path, "wb") as f:
        while True:
            chunk = resp.read(1024 * 1024)
            if not chunk:
                break
            written += len(chunk)
            if written > max_bytes:
                raise ValueError(f"download exceeded the {max_bytes}-byte cap mid-transfer")
            f.write(decryptor.update(chunk))
        f.write(decryptor.finalize())
    return written


MEGA_KEY_IN_TEXT = re.compile(r"(mega\.nz/file/[a-zA-Z0-9_-]+)#\S*")


def scrub(text):
    """A MEGA fragment is the file's decryption key -- it must not reach a
    log line, a workflow annotation or an issue comment. Applied to whole
    messages, not just URLs, because urllib embeds the URL in some of its
    own error strings."""
    return MEGA_KEY_IN_TEXT.sub(r"\1#<redacted>", text)


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    url = argv[1]
    out_path = argv[2]
    max_bytes = None
    allowlist_path = DEFAULT_ALLOWLIST
    rest = argv[3:]
    i = 0
    while i < len(rest):
        if rest[i] == "--max-bytes":
            max_bytes = int(rest[i + 1])
            i += 2
        elif rest[i] == "--allowlist":
            allowlist_path = rest[i + 1]
            i += 2
        else:
            print(f"unknown argument: {rest[i]}", file=sys.stderr)
            return 2
    if max_bytes is None:
        print("--max-bytes is required", file=sys.stderr)
        return 2

    hosts = load_allowlist(allowlist_path)
    try:
        hostname = validate_url_shape(url)
    except ValueError as e:
        print(f"rejected URL: {e}", file=sys.stderr)
        return 1
    entry = match_host(url, hosts)
    if entry is None:
        print(f"host not allow-listed: {hostname}", file=sys.stderr)
        return 1

    try:
        if entry["kind"] == "google-drive":
            written = fetch_google_drive(url, hosts, out_path, max_bytes)
        elif entry["kind"] == "mediafire":
            written = fetch_mediafire(url, hosts, out_path, max_bytes)
        elif entry["kind"] == "dropbox":
            written = fetch_dropbox(url, hosts, out_path, max_bytes)
        elif entry["kind"] == "mega":
            written = fetch_mega(url, hosts, out_path, max_bytes)
        else:
            written = fetch_direct(url, hosts, out_path, max_bytes)
    except ImportError as e:
        print(f"download failed: missing dependency ({e})", file=sys.stderr)
        return 1
    except (ValueError, urllib.error.URLError, OSError) as e:
        # scrub(), not str(e) alone: a MEGA URL carries its decryption key
        # in the fragment and urllib puts the URL in some of its messages.
        print(f"download failed: {scrub(str(e))}", file=sys.stderr)
        return 1

    print(f"wrote {out_path} ({written} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
