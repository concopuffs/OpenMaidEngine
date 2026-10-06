# Third-party notices and research acknowledgments

This file records incorporated third-party code and prior public research that materially informed this
project. It does not license Eushully game assets or code, and no original game files are distributed with
this repository. The repository's MIT license covers authored project work only; material identified below
remains subject to its own terms or is acknowledged as prior research rather than relicensed here.

## FFmpeg

Release packages include [FFmpeg](https://ffmpeg.org/) 8.1.3 shared libraries (`libavformat`, `libavcodec`,
`libavutil`, `libswscale`, `libswresample`), licensed under the GNU Lesser General Public License version 2.1 or
later. The project does not own FFmpeg. The libraries are built unmodified from the official release tarball by
`native/age_movie_ffmpeg/build-ffmpeg-sdk.sh`, configured without GPL, nonfree, version-3, or external-library
components, and the movie shim links them dynamically. The exact source tarball, both platform SDKs, and their
build configurations are published together on this repository's `deps-ffmpeg-ome-8.1.3-mpeg1-r1` release; the
pinned source and configure flags are recorded in `native/age_movie_ffmpeg/ffmpeg-source.json`. Every package
ships the license as `FFmpeg-LICENSE.txt` and the source location and configure line as `FFmpeg-SOURCE.txt` beside
the libraries. The source repository itself contains no FFmpeg code.

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

`engine/Age.Engine/Sys4/AgfDecoder.cs` ports the AGF decoding algorithm from GARbro's
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
