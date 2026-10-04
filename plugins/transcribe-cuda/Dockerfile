# transcribe.cpp's libraries built with CUDA, and nothing else: an image FROM scratch holding
# the shared libraries (libtranscribe.so and ggml's), for an image of speech to text to copy
# instead of compiling them. PyPI ships transcribe.cpp built for the CPU only (its CUDA
# provider is not published yet), and the build takes long, so it is done once here.
#
# Used as:  COPY --from=ghcr.io/berpiztu/transcribe-cuda:0.3.1-sm86 /lib /opt/transcribe/lib
# on an nvidia/cuda runtime image of the same CUDA (12.8), with the transcribe-cpp binding
# installed --no-deps and TRANSCRIBE_LIBRARY pointing at libtranscribe.so.

FROM nvidia/cuda:12.8.1-devel-ubuntu24.04 AS build
RUN apt-get update && apt-get install -y --no-install-recommends git cmake build-essential ca-certificates \
    && rm -rf /var/lib/apt/lists/*
# The release of transcribe.cpp, and the GPU architecture: 86 = RTX 30xx, 89 = RTX 40xx,
# 120 = RTX 50xx, or several separated by ';' (each one adds to the build time)
ARG TRANSCRIBE_VERSION=v0.3.1
ARG CUDA_ARCHS=86
RUN git clone --depth 1 --branch ${TRANSCRIBE_VERSION} https://github.com/handy-computer/transcribe.cpp /src
WORKDIR /src
RUN cmake -B build -DCMAKE_BUILD_TYPE=Release \
        -DTRANSCRIBE_CUDA=ON -DTRANSCRIBE_BUILD_SHARED=ON \
        -DTRANSCRIBE_BUILD_TESTS=OFF -DTRANSCRIBE_BUILD_EXAMPLES=OFF \
        -DCMAKE_CUDA_ARCHITECTURES="${CUDA_ARCHS}" \
    && cmake --build build --target transcribe -j"$(nproc)" \
    && mkdir /out && find build -name "*.so*" -exec cp -P {} /out/ \;

FROM scratch
COPY --from=build /out /lib
