import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("comparison", Path(__file__).with_name("compare-native.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class CalibrationTests(unittest.TestCase):
    def reports(self):
        calibration = {"base": "a", "head": "a", "calibration": True, "pairs": 5,
                       "environment": {"runtime": "test"}, "runtime_mode": "optimized-jit",
                       "results": [{"scenario": "work", "paired_time_ratio_min": 0.98, "paired_time_ratio_max": 1.02}]}
        report = copy.deepcopy(calibration)
        report.update(base="b", calibration=False)
        return report, calibration

    def test_consistently_slow_result_is_flagged(self):
        report, calibration = self.reports()
        report["results"][0]["paired_time_ratio_min"] = 1.2
        self.assertTrue(module.calibrated_screen(report, calibration)[0]["flagged"])

    def test_noise_or_mixed_results_not_flagged(self):
        report, calibration = self.reports()
        report["results"][0]["paired_time_ratio_min"] = 1.01
        self.assertFalse(module.calibrated_screen(report, calibration)[0]["flagged"])

    def test_wrong_revision_environment_mode_or_pair_count_rejected(self):
        for field, value in (("head", "wrong"), ("environment", {}), ("runtime_mode", "runtime-default"), ("pairs", 4), ("calibration", False)):
            with self.subTest(field=field):
                report, calibration = self.reports()
                calibration[field] = value
                with self.assertRaises(ValueError): module.calibrated_screen(report, calibration)

    def test_invalid_ratios_rejected(self):
        for value in (0, -1, float('nan'), float('inf'), True):
            report, calibration = self.reports()
            calibration["results"][0]["paired_time_ratio_min"] = value
            with self.assertRaises(ValueError): module.calibrated_screen(report, calibration)

if __name__ == '__main__': unittest.main()
