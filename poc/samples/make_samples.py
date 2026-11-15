"""Generate sample ZIP files covering the receiving routes and ZIP cases in docs/PRD.md.

Usage:
    python make_samples.py [output_dir]

Requires 7-Zip (or NanaZip) on PATH as ``7z`` for the encrypted samples.
Every encrypted sample uses the password in ``PASSWORD``. Output files are
overwritten on each run; AES samples differ between runs because of their salt.
"""

import struct
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

PASSWORD = "Sample-Pass-2026"

PAYLOAD = {
    "図面/A-001_平面図.txt": "サンプルの平面図です。\n",
    "図面/A-002_立面図.txt": "サンプルの立面図です。\n",
    "仕様書.txt": "サンプルの仕様書です。\n",
    "readme.txt": "This is a sample file for ppapd.\n",
}

PASSWORD_MAIL = f"""お世話になっております。

先ほどお送りしたファイルのパスワードをお知らせします。

パスワード：{PASSWORD}

よろしくお願いいたします。
"""


class Cp932ZipInfo(zipfile.ZipInfo):
    """ZipInfo that stores the file name in CP932 without the UTF-8 flag, as older Japanese tools do."""

    def _encodeFilenameFlags(self):
        return self.filename.encode("cp932"), self.flag_bits & ~0x800


def write_payload(root: Path) -> None:
    for name, text in PAYLOAD.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8", newline="\n")


def plain_utf8(out: Path) -> None:
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, text in PAYLOAD.items():
            archive.writestr(name, text.encode("utf-8"))


def plain_cp932(out: Path) -> None:
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, text in PAYLOAD.items():
            archive.writestr(Cp932ZipInfo(name), text.encode("utf-8"))


def zip_slip(out: Path) -> None:
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("readme.txt", PAYLOAD["readme.txt"].encode("utf-8"))
        archive.writestr("../../outside.txt", b"This entry points outside the destination.\n")


def encrypted_utf8(out: Path, payload_root: Path, method: str) -> None:
    out.unlink(missing_ok=True)
    subprocess.run(
        ["7z", "a", "-tzip", f"-p{PASSWORD}", f"-mem={method}", "-mcu=on", str(out.resolve()), "."],
        cwd=payload_root,
        check=True,
        stdout=subprocess.DEVNULL,
    )


def rename_to_cp932(src: Path, out: Path) -> None:
    """Copy a ZIP, re-encoding UTF-8 flagged entry names as CP932 and clearing the flag.

    The entry data is copied byte for byte, so encryption is untouched: neither
    ZipCrypto nor WinZip AES covers the file name. 7-Zip and NanaZip always
    write non-ASCII names in encrypted ZIPs as UTF-8, hence this rewrite.
    """
    data = src.read_bytes()
    with zipfile.ZipFile(src) as archive:
        start_dir = archive.start_dir
        entry_count = len(archive.infolist())

    local_parts, central_parts = [], []
    offset, position = 0, start_dir
    for _ in range(entry_count):
        assert data[position : position + 4] == b"PK\x01\x02"
        flags = struct.unpack_from("<H", data, position + 8)[0]
        name_len, extra_len, comment_len = struct.unpack_from("<HHH", data, position + 28)
        local_offset = struct.unpack_from("<I", data, position + 42)[0]
        compress_size = struct.unpack_from("<I", data, position + 20)[0]
        name = data[position + 46 : position + 46 + name_len]
        central_rest = data[position + 46 + name_len : position + 46 + name_len + extra_len + comment_len]
        assert not flags & 0x8, "data descriptors are not supported"

        new_flags = flags & ~0x800
        new_name = name.decode("utf-8").encode("cp932") if flags & 0x800 else name

        local_name_len, local_extra_len = struct.unpack_from("<HH", data, local_offset + 26)
        local_extra = data[local_offset + 30 + local_name_len : local_offset + 30 + local_name_len + local_extra_len]
        body_start = local_offset + 30 + local_name_len + local_extra_len
        local_header = bytearray(data[local_offset : local_offset + 30])
        struct.pack_into("<H", local_header, 6, new_flags)
        struct.pack_into("<H", local_header, 26, len(new_name))
        local = bytes(local_header) + new_name + local_extra + data[body_start : body_start + compress_size]

        central_header = bytearray(data[position : position + 46])
        struct.pack_into("<H", central_header, 8, new_flags)
        struct.pack_into("<H", central_header, 28, len(new_name))
        struct.pack_into("<I", central_header, 42, offset)
        central_parts.append(bytes(central_header) + new_name + central_rest)

        local_parts.append(local)
        offset += len(local)
        position += 46 + name_len + extra_len + comment_len

    central = b"".join(central_parts)
    end = struct.pack("<4s4H2LH", b"PK\x05\x06", 0, 0, entry_count, entry_count, len(central), offset, 0)
    out.write_bytes(b"".join(local_parts) + central + end)


def minimal_pdf(out: Path) -> None:
    """Write a one-page PDF with no text, valid enough for a mail client to preview."""
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>",
    ]
    body, offsets = b"%PDF-1.4\n", []
    for number, obj in enumerate(objects, start=1):
        offsets.append(len(body))
        body += b"%d 0 obj\n" % number + obj + b"\nendobj\n"
    xref = len(body)
    body += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
    body += b"".join(b"%010d 00000 n \n" % o for o in offsets)
    body += b"trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%EOF\n" % (len(objects) + 1, xref)
    out.write_bytes(body)


def main() -> None:
    out_dir = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).parent / "out")
    out_dir.mkdir(parents=True, exist_ok=True)

    plain_utf8(out_dir / "plain-utf8.zip")
    plain_cp932(out_dir / "plain-cp932.zip")
    zip_slip(out_dir / "zip-slip.zip")

    with tempfile.TemporaryDirectory() as tmp:
        payload_root = Path(tmp)
        write_payload(payload_root)
        for method, label in (("ZipCrypto", "zipcrypto"), ("AES256", "aes256")):
            utf8 = out_dir / f"ppap-{label}-utf8.zip"
            encrypted_utf8(utf8, payload_root, method)
            rename_to_cp932(utf8, out_dir / f"ppap-{label}-cp932.zip")

    (out_dir / "password-mail.txt").write_text(PASSWORD_MAIL, encoding="utf-8")
    minimal_pdf(out_dir / "sample.pdf")


if __name__ == "__main__":
    main()
