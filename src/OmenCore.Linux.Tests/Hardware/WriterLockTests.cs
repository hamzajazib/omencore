using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

public class WriterLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omencore-lock-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string LockPath => Path.Combine(_dir, "writer.lock");

    [Fact]
    public void SecondWriter_IsRefused_AndToldWhoHoldsTheLock()
    {
        using var first = WriterLock.TryAcquire(LockPath, "daemon", out _);
        first.Should().NotBeNull();

        var second = WriterLock.TryAcquire(LockPath, "cli fan", out var heldBy);

        second.Should().BeNull();
        heldBy.Should().Contain("daemon").And.Contain(Environment.ProcessId.ToString());
    }

    [Fact]
    public void LockCanBeTakenAgain_OnceTheOwnerReleasesIt()
    {
        var first = WriterLock.TryAcquire(LockPath, "daemon", out _);
        first!.Dispose();

        using var again = WriterLock.TryAcquire(LockPath, "cli fan", out _);

        again.Should().NotBeNull();
    }

    [Fact]
    public void ReleasingTheLock_RemovesTheInfoFile()
    {
        var first = WriterLock.TryAcquire(LockPath, "daemon", out _);
        File.Exists(LockPath + ".info").Should().BeTrue();

        first!.Dispose();

        File.Exists(LockPath + ".info").Should().BeFalse();
    }
}
