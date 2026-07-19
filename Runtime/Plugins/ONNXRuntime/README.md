# ONNX Runtime binary dependency

This directory contains the CPU-only ONNX Runtime files used by the imported
Quickstart sample. The package pins Microsoft.ML.OnnxRuntime `1.24.4`, built
from upstream commit `2d924974ef147392ced8409d36bd6d2e7fcc8a74`.

The files are the exact binaries previously validated by EnvForge:

| File | SHA-256 |
| --- | --- |
| `Managed/Microsoft.ML.OnnxRuntime.dll` | `5c3c531af36a6cb4baa01db20dcf94a0464ec36f8090aa42f35ff565b90a1ea6` |
| `Windows/x86_64/onnxruntime.dll` | `b95efb2113b603bbbf3f191061c5516a871ed546893c820e4f3b7b6c358dbf2a` |
| `Windows/x86_64/onnxruntime_providers_shared.dll` | `f2540b89707b47895c2a732bfd04e34a695c580d22301ef44c0f01f09b001673` |

Unity importer metadata enables the native libraries only for Windows x64
Editor and Windows x64 Standalone. Unity 6000.3.11f1 on Windows x64 is the only
initially verified Unity target. The managed assembly may compile on other
targets, but this package does not provide their native runtime libraries and
the Quickstart must not be presented as supported there.

ONNX Runtime is licensed under the MIT License. See `LICENSE.txt` and the
matching upstream `ThirdPartyNotices.txt` in this directory. The package does
not include Sentis, a model converter, or another inference runtime.
