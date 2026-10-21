# wslc

The container images this project makes besides the agent itself: each folder
holds what one image is built from. They are built and pushed by GitHub, by
hand (Actions, Run workflow), on their own clock and not the agent's releases.

| Folder | Image | Built by | Its container |
| --- | --- | --- | --- |
| [plugins/wslc-published](plugins/wslc-published/README.md) | `ghcr.io/berpiztu/wslc-published` | Actions, *Published proxy image*: the version, the next one by itself when left empty, and latest | the publishing proxy, made by the agent's publishing Set up; brought to a new image by its **Update image** |
| [plugins/transcribe-cuda](plugins/transcribe-cuda/README.md) | `ghcr.io/berpiztu/transcribe-cuda:<version>-sm<GPU>` | Actions, *transcribe.cpp CUDA image* | none: images of speech to text copy its libraries |

A new image reaches a container by **Update image** on it, in the agent, set to
`latest`: the image is pulled and the container made again on it with its
ports, mounts, environment, GPU and name. The proxy's Dockerfile upgrades its
system packages, so every build carries the fixes Debian has made since its
base image was published; transcribe-cuda's holds no system to upgrade.
