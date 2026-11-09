# Third-party notices and research acknowledgments

This file records incorporated third-party code and prior public research that materially informed this
project. It does not license Eushully game assets or code, and no original game files are distributed with
this repository. The repository's MIT license covers authored project work only; material identified below
remains subject to its own terms or is acknowledged as prior research rather than relicensed here.

Release packages also carry each shipped component's own license texts, named in its section below. Every NuGet
package a release ships is named below in backticks; the packagers refuse a package that leaves one out
(`docs/tools-reference.md`, "Third-party notices"). A package's managed directory is `data_OME_<platform>_x86_64`.

## Godot Engine

Release packages are exported with the official [Godot Engine](https://godotengine.org/) 4.7-stable .NET export
templates and ship the Godot runtime and its `GodotSharp` assembly: Copyright (c) 2014-present Godot Engine
contributors, Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur, under the MIT (Expat) License. The Godot runtime
includes third-party components under their own licenses, among them FreeType, HarfBuzz, ENet, Mbed TLS, Brotli,
libpng, zlib, Zstandard, and libogg and libvorbis, which play the games' Ogg Vorbis audio. Every package ships
Godot's `LICENSE.txt` as `Godot-LICENSE.txt` and its `COPYRIGHT.txt` as `Godot-COPYRIGHT.txt`; the latter lists
every component with its copyright holders and gives each license's full text. The repository keeps both files
unmodified in `notices/godot/`, pinned (`notices.json`) to the release tag that `tools/godot-linux-x64.json`
installs.

Portions of this software are copyright © 1996-2025 The FreeType Project (www.freetype.org). All rights reserved.

## .NET

Release packages are self-contained. They include the .NET 8 runtime (the `Microsoft.NETCore.App` runtime pack)
and the `System.Text.Encoding.CodePages` NuGet package, both Copyright (c) .NET Foundation and Contributors, under
the MIT License. The runtime includes third-party components listed in its notices file. The publish copies each
shipped NuGet package's own license and notice files into the managed directory as `<package id>-<file name>`:
`Microsoft.NETCore.App.Runtime.<rid>-LICENSE.TXT` and `Microsoft.NETCore.App.Runtime.<rid>-THIRD-PARTY-NOTICES.TXT`
for the runtime, and `System.Text.Encoding.CodePages-THIRD-PARTY-NOTICES.TXT`. That package carries no license file
of its own; its MIT license is the runtime's.

## FFmpeg

Release packages include [FFmpeg](https://ffmpeg.org/) 8.1.3 shared libraries (`libavformat`, `libavcodec`,
`libavutil`, `libswscale`, `libswresample`), licensed under the GNU Lesser General Public License version 2.1 or
later. The project does not own FFmpeg. The libraries are built unmodified from the official release tarball by
`native/ome_movie_ffmpeg/build-ffmpeg-sdk.sh`, configured without GPL, nonfree, version-3, or external-library
components, and the movie shim links them dynamically. The exact source tarball, both platform SDKs, and their
build configurations are published together on this repository's `deps-ffmpeg-ome-8.1.3-mpeg1-r1` release; the
pinned source and configure flags are recorded in `native/ome_movie_ffmpeg/ffmpeg-source.json`. Every package
ships the license as `FFmpeg-LICENSE.txt` and the source location and configure line as `FFmpeg-SOURCE.txt` beside
the libraries. The source repository itself contains no FFmpeg code.

## MinGW-w64 and GCC runtimes

Windows release packages' movie shim (`ome_movie_ffmpeg.dll`) and FFmpeg DLLs are built with MinGW-w64 GCC 13,
which links parts of the [MinGW-w64](https://www.mingw-w64.org/) 11.0.1 runtime into each DLL: the DLL startup code,
the C99 `printf` and `scanf` family with David M. Gay's gdtoa number conversions, and math functions. Most of the
runtime is public domain. The linked parts that are not are the x86 math functions and `scanf` (Copyright (c)
2009-2011 by the mingw-w64 project, under the Zope Public License 2.1) and gdtoa (Copyright (C) 1997-2001 by Lucent
Technologies; Copyright (C) 2004 by David M. Gay). Their notices are in the runtime's
`COPYING.MinGW-w64-runtime.txt`, which every Windows package ships unmodified as `MinGW-w64-runtime-COPYING.txt`.
The repository keeps that file in `notices/mingw-w64/`, pinned (`notices.json`) to the runtime's v11.0.1 tag. The
DLLs also contain GCC's runtime helpers (`libgcc`, `crtbegin.o`, `crtend.o`; Copyright (C) Free Software
Foundation, Inc.), under GPLv3 with the GCC Runtime Library Exception 3.1, which puts no conditions on the DLLs.
Each MinGW build checks every runtime object it links against this list (`docs/tools-reference.md`, "Third-party
notices"). Linux packages link no MinGW code.

## Eushully-Decompiler research

Early SYS4 instruction decoding and the initial opcode ABI/catalog were informed by Kelebek's public
[Eushully-Decompiler](https://github.com/Kelebek1/Eushully-Decompiler) project. That work established a
valuable starting point and accelerated this reimplementation. Historical and per-opcode provenance is
preserved in `vm-map/opcodes.toml`.

This repository does not vendor the upstream source files. Its opcode registry combines that foundational
catalog with independent script-corpus analysis, native `AGE.EXE` investigation, runtime probes, and
reimplementation testing. This acknowledgment is not a claim that the project was developed under a formal
clean-room process.

## GARbro AGF decoding algorithm

`engine/Ome.Engine/Sys4/AgfDecoder.cs` ports the AGF decoding algorithm from GARbro's
`ArcFormats/Eushully/ImageAGF.cs`, Copyright (c) 2014-2020 morkt, under the MIT License:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction, including
> without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
> following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial
> portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
> LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
> EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
> IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
> USE OR OTHER DEALINGS IN THE SOFTWARE.

## Tomlyn

`engine/Ome.Mods` reads mod manifests (`manifest.toml`) with the [Tomlyn](https://github.com/xoofx/Tomlyn) 2.10.1
NuGet package (`Tomlyn`), Copyright (c) Alexandre Mutel, under the BSD 2-Clause License. The package is referenced, not
vendored; builds that include `Ome.Mods` ship its unmodified `Tomlyn.dll`:

> Redistribution and use in source and binary forms, with or without modification, are permitted provided that
> the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the
>    following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the
>    following disclaimer in the documentation and/or other materials provided with the distribution.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED
> WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
> PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
> INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
> SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
> THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING
> IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
