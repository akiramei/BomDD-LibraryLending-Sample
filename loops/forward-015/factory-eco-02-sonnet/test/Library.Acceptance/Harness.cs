namespace Library.Acceptance;

/// <summary>Minimal PASS/FAIL accumulator. Prints each result; exit non-zero if any FAIL.</summary>
public sealed class Harness
{
    public int Passed { get; private set; }
    public int Failed { get; private set; }

    public void Check(string name, bool condition)
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine($"PASS  {name}");
        }
        else
        {
            Failed++;
            Console.WriteLine($"FAIL  {name}");
        }
    }
}
