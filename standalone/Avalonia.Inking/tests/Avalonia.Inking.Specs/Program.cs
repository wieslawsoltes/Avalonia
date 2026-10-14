using Avalonia.Inking.Specs;
var report = InkSpecs.Run();
var json = InkSpecs.ToJson(report);
if (args.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); File.WriteAllText(args[0], json); }
Console.WriteLine($"Ink core: {report.Count(r => r.Passed)}/{report.Count} passed; {report.Count(r => !r.Passed)} failed.");
foreach (var result in report.Where(r => !r.Passed)) Console.WriteLine($"FAIL {result.Name}: {result.Detail}");
return report.Any(r => !r.Passed) ? 1 : 0;
