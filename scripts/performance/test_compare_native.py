"""Unit tests for measurement validation; does not require .NET or a remote repository."""
import contextlib
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("compare_native", Path(__file__).with_name("compare-native.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def payload():
    return {"schema": 1, "runtime": ".NET test", "os": "test", "architecture": "X64",
            "processorCount": 4, "backend": "test", "runtimeMode": "optimized-jit",
            "results": [{"Scenario": "shape-one-pass-short", "Operations": 10,
                         "Nanoseconds": 1000.0, "AllocatedBytes": 100,
                         "Checksum": 50, "PreparedRows": 0, "ClearedRows": 0}]}


class ComparisonTests(unittest.TestCase):
    def report(self, pairs):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            for index, (base, head) in enumerate(pairs):
                for label, value in (("base", base), ("head", head)):
                    (output / f"{label}-{index}.json").write_text(json.dumps(value), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                module.summarize(output, len(pairs), "a" * 40, "b" * 40)
            return json.loads((output / "comparison.json").read_text(encoding="utf-8"))

    def test_valid_report_keeps_paired_ratios(self):
        pairs = []
        for multiplier in (0.8, 0.5, 1.2):
            before, after = payload(), payload()
            after["results"][0]["Nanoseconds"] *= multiplier
            pairs.append((before, after))
        result = self.report(pairs)
        self.assertAlmostEqual(-20, result["results"][0]["paired_time_delta_percent"])
        self.assertEqual(3, result["pairs"])
        self.assertEqual("report-only", result["timing_policy"])

    def test_invalid_numeric_values_rejected(self):
        invalid = {"Operations": [0, -1, True, 1.5], "Nanoseconds": [0, -1, float("nan"), float("inf"), True],
                   "AllocatedBytes": [-1, True, "100"], "PreparedRows": [-1], "ClearedRows": [-1], "Checksum": [None]}
        for field, values in invalid.items():
            for value in values:
                with self.subTest(field=field, value=value):
                    data = payload()
                    data["results"][0][field] = value
                    with self.assertRaises(ValueError):
                        module.validate_payload(data, "test", "optimized-jit")

    def test_duplicate_and_empty_results_rejected(self):
        for results in ([], None, [payload()["results"][0]] * 2):
            with self.subTest(results=results):
                data = payload()
                data["results"] = results
                with self.assertRaises(ValueError):
                    module.validate_payload(data, "test", "optimized-jit")

    def test_changed_logical_output_rejected(self):
        for field in ("Operations", "Checksum", "PreparedRows", "ClearedRows"):
            with self.subTest(field=field):
                before, after = payload(), payload()
                after["results"][0][field] += 1
                with self.assertRaisesRegex(ValueError, "Logical output differs"):
                    self.report([(before, after)])

    def test_scenario_disappearing_between_pairs_rejected(self):
        initial = payload()
        other = copy.deepcopy(initial["results"][0])
        other["Scenario"] = "other"
        initial["results"].append(other)
        with self.assertRaisesRegex(ValueError, "Scenario set changed"):
            self.report([(initial, initial), (payload(), payload())])

    def test_environment_drift_rejected(self):
        for field, value in (("runtime", "different"), ("architecture", "Arm64"), ("processorCount", 8)):
            with self.subTest(field=field):
                before, after = payload(), payload()
                after[field] = value
                with self.assertRaisesRegex(ValueError, "Environment changed"):
                    self.report([(before, after)])

    def test_mode_and_missing_environment_rejected(self):
        for field in module.ENVIRONMENT_FIELDS:
            with self.subTest(field=field):
                data = payload()
                del data[field]
                with self.assertRaises(ValueError):
                    module.validate_payload(data, "test", "optimized-jit")
        with self.assertRaises(ValueError):
            module.validate_payload(payload(), "test", "runtime-default")

    def test_runtime_modes_do_not_inherit_known_overrides(self):
        with patch.dict(os.environ, {"DOTNET_TieredCompilation": "1", "COMPlus_TieredPGO": "0", "DOTNET_ReadyToRun": "0"}):
            default = module.measurement_environment("runtime-default")
            self.assertNotIn("DOTNET_TieredCompilation", default)
            self.assertNotIn("COMPlus_TieredPGO", default)
            self.assertNotIn("DOTNET_ReadyToRun", default)
            self.assertEqual("0", module.measurement_environment("optimized-jit")["DOTNET_TieredCompilation"])
        with self.assertRaises(ValueError):
            module.measurement_environment("invalid")


if __name__ == "__main__":
    unittest.main()
