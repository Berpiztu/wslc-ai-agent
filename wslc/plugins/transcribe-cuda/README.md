# transcribe-cuda

**Image:** `ghcr.io/berpiztu/transcribe-cuda:<transcribe.cpp version>-sm<GPU architecture>`,
for instance `0.3.1-sm86`.

[transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) (MIT) built with
CUDA, ready to copy into an image of speech to text. PyPI ships it built for the CPU
only, and compiling the CUDA kernels takes long, so it is built once here and copied
wherever it is needed.

The image holds nothing but the shared libraries, in `/lib`: no CUDA, no operating
system. It is not run; another image copies from it:

```dockerfile
FROM nvidia/cuda:12.8.1-runtime-ubuntu24.04
# ... python3, the transcribe-cpp binding installed with --no-deps ...
COPY --from=ghcr.io/berpiztu/transcribe-cuda:0.3.1-sm86 /lib /opt/transcribe/lib
ENV TRANSCRIBE_LIBRARY=/opt/transcribe/lib/libtranscribe.so \
    LD_LIBRARY_PATH=/opt/transcribe/lib
```

The runtime image needs the same CUDA it was built with (12.8), and a binding of the
same transcribe.cpp version (`pip install --no-deps transcribe-cpp==0.3.1`). On WSLC the
container runs with `--gpus all`, which brings the Windows driver's `libcuda` into
`/usr/lib/wsl/lib`: add that folder to `LD_LIBRARY_PATH` too.

## GPU architectures

Each architecture is compiled separately and adds to the build time, so the image is
built for the one in use: `86` for an RTX 30xx, `89` for an RTX 40xx, `120` for an
RTX 50xx, or several separated by `;`. The architecture is part of the tag.

## Building it

By hand only: Actions → "transcribe.cpp CUDA image" → Run workflow, with the
transcribe.cpp release and the architecture. A new image is needed only for a new
release of transcribe.cpp or another graphics card.
