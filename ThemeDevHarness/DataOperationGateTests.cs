using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyDataOperationGate()
    {
        void Check(bool good, string label) { if (!good) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var gate = new DataOperationGate(); var drained = 0;
        gate.Drained += () => drained++;
        var first = gate.Begin()!; var second = gate.Begin()!;
        Check(!gate.RequestClose() && gate.ActiveCount == 2, "close deferred while operations active");
        Check(gate.Begin() is null, "new writes rejected after close request");
        first.Dispose(); Check(drained == 0 && gate.ActiveCount == 1, "wait for every operation");
        first.Dispose(); Check(gate.ActiveCount == 1, "duplicate dispose is harmless");
        Check(!gate.RequestClose(), "repeated close stays deferred");
        second.Dispose(); Check(drained == 1 && gate.ActiveCount == 0, "shutdown notified once after cleanup");
        var failure = new DataOperationGate(); var cleaned = false;
        failure.Drained += () => Check(cleaned, "failure cleanup precedes shutdown");
        try
        {
            using var scope = failure.Begin(); failure.RequestClose();
            try { throw new InvalidOperationException("test"); } finally { cleaned = true; }
        }
        catch (InvalidOperationException) { }
        Check(failure.ActiveCount == 0, "exception releases protection");
        Check(new DataOperationGate().RequestClose(), "idle close allowed immediately");
    }
}
