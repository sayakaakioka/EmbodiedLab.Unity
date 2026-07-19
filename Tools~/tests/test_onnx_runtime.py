from __future__ import annotations

import hashlib
import json
from pathlib import Path
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
PLUGIN_ROOT = REPOSITORY_ROOT / "Runtime" / "Plugins" / "ONNXRuntime"


class OnnxRuntimePackagingTests(unittest.TestCase):
    def test_exact_proven_binaries_are_packaged(self) -> None:
        expected = {
            "Managed/Microsoft.ML.OnnxRuntime.dll": (
                228_376,
                "5c3c531af36a6cb4baa01db20dcf94a0464ec36f8090aa42f35ff565b90a1ea6",
            ),
            "Windows/x86_64/onnxruntime.dll": (
                14_203_464,
                "b95efb2113b603bbbf3f191061c5516a871ed546893c820e4f3b7b6c358dbf2a",
            ),
            "Windows/x86_64/onnxruntime_providers_shared.dll": (
                22_088,
                "f2540b89707b47895c2a732bfd04e34a695c580d22301ef44c0f01f09b001673",
            ),
        }
        for relative_path, (size, sha256) in expected.items():
            path = PLUGIN_ROOT / relative_path
            self.assertEqual(size, path.stat().st_size, relative_path)
            self.assertEqual(
                sha256,
                hashlib.sha256(path.read_bytes()).hexdigest(),
                relative_path,
            )

    def test_native_importers_are_windows_x64_only(self) -> None:
        for filename in (
            "onnxruntime.dll.meta",
            "onnxruntime_providers_shared.dll.meta",
        ):
            metadata = (PLUGIN_ROOT / "Windows" / "x86_64" / filename).read_text(
                encoding="utf-8"
            )
            self.assertIn("Editor: Editor", metadata)
            self.assertIn("OS: Windows", metadata)
            self.assertIn("CPU: x86_64", metadata)
            self.assertIn("Standalone: Win64", metadata)
            self.assertIn(
                "Standalone: Linux64\n    second:\n      enabled: 0", metadata
            )
            self.assertIn(
                "Standalone: OSXUniversal\n    second:\n      enabled: 0", metadata
            )
            self.assertIn("Standalone: Win\n    second:\n      enabled: 0", metadata)

    def test_license_notices_and_version_are_documented(self) -> None:
        license_text = (PLUGIN_ROOT / "LICENSE.txt").read_text(encoding="utf-8")
        notices = (PLUGIN_ROOT / "ThirdPartyNotices.txt").read_text(encoding="utf-8")
        readme = (PLUGIN_ROOT / "README.md").read_text(encoding="utf-8")
        self.assertIn("MIT License", license_text)
        self.assertGreater(len(notices), 300_000)
        self.assertIn("1.24.4", readme)
        self.assertIn("Windows x64", readme)

    def test_sample_references_package_owned_managed_assembly(self) -> None:
        assembly = json.loads(
            (
                REPOSITORY_ROOT
                / "Samples~"
                / "Quickstart"
                / "EmbodiedLab.Unity.Samples.Quickstart.asmdef"
            ).read_text(encoding="utf-8")
        )
        self.assertIn("Microsoft.ML.OnnxRuntime.dll", assembly["precompiledReferences"])
        self.assertTrue(assembly["overrideReferences"])


if __name__ == "__main__":
    unittest.main()
