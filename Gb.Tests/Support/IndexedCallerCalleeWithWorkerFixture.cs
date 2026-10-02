namespace Gb.Tests;

public sealed class IndexedCallerCalleeWithWorkerFixture : IndexedCallerCalleeFixture
{
    public IndexedCallerCalleeWithWorkerFixture()
        : base(includeWorker: true)
    {
    }
}
