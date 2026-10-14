using Avalonia.Inking.Specs;
var report = InkSpecs.Run();
var json = InkSpecs.ToJson(report);
Console.WriteLine(json);
if (args.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); File.WriteAllText(args[0], json); }
return report.Any(r => !r.Passed) ? 1 : 0;
