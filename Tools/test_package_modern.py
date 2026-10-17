import plistlib
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from package_modern import BUNDLE, package


class Packages(unittest.TestCase):
    def test_all_platforms(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for rid in ("win-x64", "linux-x64", "osx-arm64", "osx-x64"):
                published = root / rid
                published.mkdir()
                exe = "PKHeX.Modern.exe" if rid == "win-x64" else "PKHeX.Modern"
                (published / exe).write_bytes(b"executable fixture")
                (published / "LICENSE.txt").write_text("fixture", encoding="utf-8")
                with self.subTest(rid=rid), patch("package_modern.make_icon", return_value=False):
                    result = package(rid, published, root / "output", "0.4.5", root / "icon.png")
                    self.assertEqual(result.name, f"PKHeX.Modern-{rid}.zip")
                    with zipfile.ZipFile(result) as archive:
                        prefix = f"{BUNDLE}/Contents/MacOS/" if rid.startswith("osx-") else ""
                        self.assertEqual(archive.read(prefix + exe), b"executable fixture")
                        self.assertEqual(archive.read(prefix + "LICENSE.txt"), b"fixture")
                        mode = archive.getinfo(prefix + exe).external_attr >> 16
                        self.assertTrue(stat.S_ISREG(mode))
                        self.assertEqual(mode & 0o777, 0o644 if rid == "win-x64" else 0o755)
                        if rid.startswith("osx-"):
                            metadata = plistlib.loads(archive.read(f"{BUNDLE}/Contents/Info.plist"))
                            self.assertEqual(metadata["CFBundleExecutable"], exe)
                            self.assertEqual(metadata["CFBundleIdentifier"], "io.github.carlosmozart.pkhexmodern")
                            self.assertEqual(metadata["CFBundleShortVersionString"], "0.4.5")
                            self.assertEqual(metadata["LSMinimumSystemVersion"], "14.0")
                            self.assertNotIn("CFBundleIconFile", metadata)

    def test_macos_bundle_signed_when_codesign_exists(self):
        calls = []
        def fake_run(args, **kwargs):
            calls.append(args)
            if args[0] == "ditto":
                Path(args[-1], BUNDLE).mkdir(parents=True)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            published = root / "osx-arm64"; published.mkdir()
            (published / "PKHeX.Modern").write_bytes(b"executable fixture")
            with patch("package_modern.make_icon", return_value=False),                  patch("package_modern.shutil.which", return_value="/usr/bin/tool"),                  patch("package_modern.subprocess.run", side_effect=fake_run):
                package("osx-arm64", published, root / "output", "0.4.5", root / "icon.png")
        self.assertTrue(any(c[:4] == ["codesign", "--force", "--deep", "--sign"] and c[4] == "-" and c[-1].endswith(BUNDLE) for c in calls))
        self.assertTrue(any(c[0] == "ditto" for c in calls))
        self.assertTrue(sum(1 for c in calls if c[:2] == ["codesign", "--verify"]) == 2)

    def test_missing_executable(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(FileNotFoundError):
                package("linux-x64", Path(folder), Path(folder), "0.4.5")


if __name__ == "__main__":
    unittest.main()
