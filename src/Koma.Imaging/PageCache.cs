namespace Koma.Imaging;

/// <summary>
/// Builds pages away from the interface thread, one at a time, and keeps only
/// the ones still wanted.
/// </summary>
/// <remarks>
/// <para>
/// One at a time because a package is a <c>ZipArchive</c>, which is not safe to
/// read from two threads at once. The build function is meant to be the only
/// code that reads the archive once the package is open, and the gate makes it
/// the only one reading it at any moment.
/// </para>
/// <para>
/// Wanted is the caller's word. <see cref="Retain"/> names the pages to keep;
/// any other is cancelled if it is not built yet, and disposed once it is,
/// however the two race. A page decoded for a spread the reader has already
/// left is never kept.
/// </para>
/// </remarks>
public sealed class PageCache<TPage> : IDisposable where TPage : class, IDisposable
{
    private readonly Func<string, TPage> build;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock sync = new();
    private readonly Dictionary<string, Entry> entries = [];
    private bool disposed;

    /// <param name="build">
    /// Builds one page. It runs on a worker thread, never two at once.
    /// </param>
    public PageCache(Func<string, TPage> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        this.build = build;
    }

    /// <summary>
    /// The page if it is built, without waiting for it.
    /// </summary>
    public TPage? TryGet(string item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (sync)
            return entries.TryGetValue(item, out Entry? entry) && entry.Build.IsCompletedSuccessfully ? entry.Build.Result : null;
    }

    /// <summary>
    /// The page, built if it is not already built or being built.
    /// </summary>
    /// <returns>
    /// A task that ends cancelled if the page is dropped by <see cref="Retain"/>
    /// before it is built.
    /// </returns>
    public Task<TPage> GetAsync(string item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (entries.TryGetValue(item, out Entry? existing))
                return existing.Build;

            var cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            Task<TPage> task = Task.Run(() => BuildAsync(item, token), token);
            entries.Add(item, new Entry(cancel, task));

            return task;
        }
    }

    /// <summary>
    /// Keeps the given pages and drops every other.
    /// </summary>
    public void Retain(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        HashSet<string> keep = [.. items];

        lock (sync)
        {
            foreach (string item in entries.Keys.Where(item => !keep.Contains(item)).ToList())
            {
                Drop(entries[item]);
                entries.Remove(item);
            }
        }
    }

    /// <remarks>
    /// Blocks until the build in flight, if any, has finished: the owner of
    /// whatever the pages are built from can dispose it straight after, with
    /// nothing left reading it.
    /// </remarks>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;

            foreach (Entry entry in entries.Values)
                Drop(entry);

            entries.Clear();
        }

        gate.Wait();
        gate.Dispose();
    }

    private async Task<TPage> BuildAsync(string item, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            token.ThrowIfCancellationRequested();

            TPage page = build(item);

            // Dropped while it was being built. Nobody can ask for it any more,
            // so it is disposed now rather than left to the collector.
            if (token.IsCancellationRequested)
            {
                page.Dispose();
                token.ThrowIfCancellationRequested();
            }

            return page;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Drop(Entry entry)
    {
        entry.Cancel.Cancel();

        // Whichever way the build ends, a page it made is disposed, even one
        // finished in the instant between the check above and the cancel.
        entry.Build.ContinueWith(static build => build.Result.Dispose(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        entry.Build.ContinueWith(_ => entry.Cancel.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record Entry(CancellationTokenSource Cancel, Task<TPage> Build);
}
