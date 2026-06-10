using Library.Acceptance;

var harness = new Harness();

Console.WriteLine("== Unit checks (CP-CORE-* test_vectors, Library.Core direct) ==");
UnitChecks.Run(harness);

Console.WriteLine();
Console.WriteLine("== L1 API smoke (6 endpoints, subprocess) ==");
await SmokeChecks.Run(harness);

Console.WriteLine();
Console.WriteLine($"RESULT: {harness.Passed} passed, {harness.Failed} failed");

return harness.Failed == 0 ? 0 : 1;
