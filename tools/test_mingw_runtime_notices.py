#!/usr/bin/env python3
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import mingw_runtime_notices as notices

GCC = "/usr/lib/gcc/x86_64-w64-mingw32/13-posix"
SYSROOT = f"{GCC}/../../../../x86_64-w64-mingw32/lib"
ROOTS = ("/usr/lib/gcc/x86_64-w64-mingw32/13-posix", "/usr/x86_64-w64-mingw32/lib")
SDK = "/work/build/ffmpeg-sdk/ome-ffmpeg-8.1.3-mpeg1-r1-win64-lgpl-shared/lib"


def link_map(*members: str, loads: tuple[str, ...] = ()) -> str:
    """A GNU ld -Map file shaped as MinGW ld writes it: the archive-member section, then the LOAD lines."""
    lines = ["Archive member included to satisfy reference by file (symbol)", ""]
    for member in members:
        if len(member) < 29:  # ld pads a short first part and keeps the reference on the same line
            lines.append(f"{member:<30}libavutil/log.o (strnlen)")
        else:
            lines += [member, "                              /tmp/ccVyUSVp.o (avio_flush)"]
    lines += ["", "Discarded input sections", "", " .text          0x0000000000000000        0x0 /tmp/ccVyUSVp.o", ""]
    lines += ["Linker script and memory map", ""]
    lines += [f"LOAD {path}" for path in loads]
    lines += ["LOAD dll stuff", ""]
    return "\n".join(lines)


SHIM_MAP = link_map(
    f"{SDK}/libavformat.dll.a(avformat_62_dll_d000099.o)",
    f"{SYSROOT}/libmingw32.a(lib64_libmingw32_a-pseudo-reloc.o)",
    f"{SYSROOT}/libmingwex.a(lib64_libmingwex_a-gdtoa.o)",
    f"{SYSROOT}/libmsvcrt.a(libmsvcrt_defs00063.o)",
    f"{SYSROOT}/libkernel32.a(libkernel32s00280.o)",
    f"{SYSROOT}/libkernel32.a(libkernel32h.o)",
    f"{GCC}/libgcc.a(_chkstk_ms.o)",
    "libav.dll.a(d1.o)",
    loads=(f"{SYSROOT}/dllcrt2.o", f"{GCC}/crtbegin.o", "/tmp/ccVyUSVp.o", f"{SDK}/libavformat.dll.a", f"{GCC}/crtend.o"),
)


class ParseLinkMapTests(unittest.TestCase):
    def test_reads_members_in_both_layouts_and_object_loads(self) -> None:
        parsed = notices.parse_link_map(SHIM_MAP)
        self.assertEqual(8, len(parsed.members))
        self.assertIn((f"{SYSROOT}/libmingwex.a", "lib64_libmingwex_a-gdtoa.o"), parsed.members)
        # A short first part keeps its reference on the same line.
        self.assertIn(("libav.dll.a", "d1.o"), parsed.members)
        self.assertEqual(
            [f"{SYSROOT}/dllcrt2.o", f"{GCC}/crtbegin.o", "/tmp/ccVyUSVp.o", f"{GCC}/crtend.o"], parsed.objects
        )

    def test_reads_paths_with_spaces(self) -> None:
        parsed = notices.parse_link_map(link_map("/opt/tool chain/lib/libmingwex.a(lib64_libmingwex_a-sin.o)"))
        self.assertEqual([("/opt/tool chain/lib/libmingwex.a", "lib64_libmingwex_a-sin.o")], parsed.members)


class ClassifyTests(unittest.TestCase):
    def test_sorts_toolchain_objects_by_their_notice_and_skips_project_inputs(self) -> None:
        covered, uncovered = notices.classify(notices.parse_link_map(SHIM_MAP), ROOTS)
        self.assertEqual([], uncovered)
        self.assertEqual(
            {
                "libmingw32.a(lib64_libmingw32_a-pseudo-reloc.o)",
                "libmingwex.a(lib64_libmingwex_a-gdtoa.o)",
                "libmsvcrt.a(libmsvcrt_defs00063.o)",
                "dllcrt2.o",
            },
            covered[notices.MINGW_RUNTIME],
        )
        self.assertEqual({"libgcc.a(_chkstk_ms.o)", "crtbegin.o", "crtend.o"}, covered[notices.GCC_RUNTIME])
        self.assertEqual(
            {"libkernel32.a(libkernel32s00280.o)", "libkernel32.a(libkernel32h.o)"}, covered[notices.IMPORT_LIBRARY]
        )

    def test_unreviewed_toolchain_code_is_uncovered(self) -> None:
        text = link_map(
            f"{SYSROOT}/libwinpthread.a(libwinpthread_la-thread.o)",
            f"{SYSROOT}/libkernel32.a(not_a_stub.o)",
            f"{GCC}/libstdc++.a(new_op.o)",
            loads=(f"{GCC}/crtfastmath.o",),
        )
        _, uncovered = notices.classify(notices.parse_link_map(text), ROOTS)
        self.assertEqual(
            [
                "libwinpthread.a(libwinpthread_la-thread.o)",
                "libkernel32.a(not_a_stub.o)",
                "libstdc++.a(new_op.o)",
                "crtfastmath.o",
            ],
            uncovered,
        )


class CompilerQueryTests(unittest.TestCase):
    def test_runtime_version_reads_the_mingw_version_macros(self) -> None:
        defines = (
            "#define __MINGW64_VERSION_MAJOR 11\n#define __MINGW64_VERSION_MINOR 0\n"
            "#define __MINGW64_VERSION_BUGFIX 1\n#define __MINGW64_VERSION_RC 0\n"
        )
        with patch.object(notices, "_compiler_output", return_value=defines):
            self.assertEqual("11.0.1", notices.runtime_version("x86_64-w64-mingw32-gcc"))
        with patch.object(notices, "_compiler_output", return_value="#define __GNUC__ 13\n"):
            with self.assertRaisesRegex(RuntimeError, "does not define"):
                notices.runtime_version("gcc")

    def test_toolchain_roots_are_the_libgcc_and_runtime_directories(self) -> None:
        answers = {
            "-print-libgcc-file-name": f"{GCC}/libgcc.a\n",
            "-print-file-name=libmingwex.a": f"{SYSROOT}/libmingwex.a\n",
        }
        with patch.object(notices, "_compiler_output", side_effect=lambda _compiler, flag: answers[flag]):
            self.assertEqual(ROOTS, notices.toolchain_roots("x86_64-w64-mingw32-gcc"))
        # GCC echoes an unknown library's bare name back.
        answers["-print-file-name=libmingwex.a"] = "libmingwex.a\n"
        with patch.object(notices, "_compiler_output", side_effect=lambda _compiler, flag: answers[flag]):
            with self.assertRaisesRegex(RuntimeError, "does not know the MinGW-w64 runtime"):
                notices.toolchain_roots("gcc")


class CheckTests(unittest.TestCase):
    def run_check(self, text: str, version: str = "11.0.1") -> str:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "age_movie_ffmpeg.map"
            path.write_text(text, encoding="utf-8")
            with patch.object(notices, "runtime_version", return_value=version), patch.object(
                notices, "toolchain_roots", return_value=ROOTS
            ):
                return notices.check("x86_64-w64-mingw32-gcc", [path])

    def test_passes_a_covered_link_against_the_pinned_runtime(self) -> None:
        self.assertEqual(
            "MINGW RUNTIME NOTICES OK: MinGW-w64 11.0.1, maps=1, runtime=4 gcc=3 import-stubs=2",
            self.run_check(SHIM_MAP),
        )

    def test_fails_an_unreviewed_object_or_another_runtime_release(self) -> None:
        with self.assertRaisesRegex(ValueError, r"age_movie_ffmpeg\.map: libwinpthread\.a\(thread\.o\)"):
            self.run_check(link_map(f"{SYSROOT}/libwinpthread.a(thread.o)"))
        with self.assertRaisesRegex(ValueError, "runtime 12.0.0, but the shipped notice is pinned to 11.0.1"):
            self.run_check(SHIM_MAP, version="12.0.0")


if __name__ == "__main__":
    unittest.main()
