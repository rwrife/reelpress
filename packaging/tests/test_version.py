import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "version.py"
SPEC = importlib.util.spec_from_file_location("reelpress_packaging_version", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
VERSION_MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERSION_MODULE)
numeric_tag_version = VERSION_MODULE.numeric_tag_version


class VersionTests(unittest.TestCase):
    def test_accepts_exact_semantic_release_tag(self) -> None:
        self.assertEqual([1, 2, 3], numeric_tag_version("v1.2.3"))

    def test_rejects_malformed_and_prerelease_tags(self) -> None:
        self.assertIsNone(numeric_tag_version("vgarbage"))
        self.assertIsNone(numeric_tag_version("v1.2.3-beta.1"))
        self.assertIsNone(numeric_tag_version("1.2.3"))

    def test_rejects_msix_component_overflow(self) -> None:
        with self.assertRaises(ValueError):
            numeric_tag_version("v65536.1.1")


if __name__ == "__main__":
    unittest.main()