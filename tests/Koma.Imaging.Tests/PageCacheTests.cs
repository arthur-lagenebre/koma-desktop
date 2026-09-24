using System.Collections.Concurrent;

namespace Koma.Imaging.Tests;

/// <summary>
/// The cache's promises: one build at a time, one build per page, and nothing
/// kept that the caller has let go of, however the timing falls.
/// </summary>
public sealed class PageCacheTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // xunit v3 wants the test's own token wherever one can be passed, so that
    // a cancelled run does not sit out a timeout.
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuildsEachPageOnce()
    {
        int builds = 0;
        using var cache = new PageCache<FakePage>(item =>
        {
            Interlocked.Increment(ref builds);
            return new FakePage(item);
        });

        Task<FakePage> first = cache.GetAsync("a");
        Task<FakePage> second = cache.GetAsync("a");
        await first.WaitAsync(Patience, Token);

        Assert.Same(first, second);
        Assert.Equal(1, builds);
    }

    [Fact]
    public async Task BuildsOnePageAtATime()
    {
        // The archive under a real build is not safe to read from two threads,
        // so two builds overlapping is the failure this guards against.
        Lock sync = new();
        int running = 0;
        int most = 0;
        using var cache = new PageCache<FakePage>(item =>
        {
            int now = Interlocked.Increment(ref running);

            lock (sync)
                most = Math.Max(most, now);

            Thread.Sleep(20);
            Interlocked.Decrement(ref running);

            return new FakePage(item);
        });
        string[] items = ["a", "b", "c", "d"];

        await Task.WhenAll(items.Select(cache.GetAsync)).WaitAsync(Patience, Token);

        Assert.Equal(1, most);
    }

    [Fact]
    public async Task TryGet_AnswersOnlyOnceThePageIsBuilt()
    {
        using var release = new ManualResetEventSlim();
        using var cache = new PageCache<FakePage>(item =>
        {
            release.Wait(Patience, Token);
            return new FakePage(item);
        });

        Task<FakePage> build = cache.GetAsync("a");

        Assert.Null(cache.TryGet("a"));

        release.Set();
        FakePage page = await build.WaitAsync(Patience, Token);

        Assert.Same(page, cache.TryGet("a"));
    }

    [Fact]
    public async Task Retain_DisposesAPageLeftBehind()
    {
        using var cache = new PageCache<FakePage>(item => new FakePage(item));
        FakePage kept = await cache.GetAsync("a").WaitAsync(Patience, Token);
        FakePage dropped = await cache.GetAsync("b").WaitAsync(Patience, Token);

        cache.Retain(["a"]);

        Assert.True(SpinWait.SpinUntil(() => dropped.IsDisposed, Patience));
        Assert.False(kept.IsDisposed);
        Assert.Null(cache.TryGet("b"));
    }

    [Fact]
    public async Task Retain_CancelsAPageNotYetBuilt()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var built = new ConcurrentBag<string>();
        using var cache = new PageCache<FakePage>(item =>
        {
            built.Add(item);
            started.Set();
            release.Wait(Patience, Token);
            return new FakePage(item);
        });

        // "a" holds the gate before "b" is asked for, so "b" is still queued
        // when it is dropped.
        Task<FakePage> first = cache.GetAsync("a");
        Assert.True(started.Wait(Patience, Token));
        Task<FakePage> queued = cache.GetAsync("b");

        cache.Retain(["a"]);
        release.Set();
        await first.WaitAsync(Patience, Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(Patience, Token));
        Assert.DoesNotContain("b", built);
    }

    [Fact]
    public async Task Retain_DisposesAPageFinishedAfterItWasDropped()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        FakePage? made = null;
        using var cache = new PageCache<FakePage>(item =>
        {
            started.Set();
            release.Wait(Patience, Token);
            return made = new FakePage(item);
        });

        Task<FakePage> build = cache.GetAsync("a");
        Assert.True(started.Wait(Patience, Token));

        cache.Retain([]);
        release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build.WaitAsync(Patience, Token));
        Assert.NotNull(made);
        Assert.True(made.IsDisposed);
    }

    [Fact]
    public void Dispose_WaitsForTheBuildInFlight()
    {
        // The owner disposes the package right after the cache. A build still
        // reading it then would read a closed archive.
        using var started = new ManualResetEventSlim();
        int finished = 0;

        // Disposed twice, the second time by the using: a second dispose does nothing.
        using var cache = new PageCache<FakePage>(item =>
        {
            started.Set();
            Thread.Sleep(200);
            Volatile.Write(ref finished, 1);
            return new FakePage(item);
        });

        _ = cache.GetAsync("a");
        Assert.True(started.Wait(Patience, Token));
        cache.Dispose();

        Assert.Equal(1, Volatile.Read(ref finished));

        // GetAsync refuses before any task exists, so the throw is synchronous
        // and the lambda discards the task it would have returned; a lambda
        // returning the task would be read as asynchronous code.
        Assert.Throws<ObjectDisposedException>(() => { _ = cache.GetAsync("b"); });
    }

    private sealed class FakePage(string item) : IDisposable
    {
        private volatile bool disposed;

        public string Item { get; } = item;

        public bool IsDisposed => disposed;

        public void Dispose() => disposed = true;
    }
}
