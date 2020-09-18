using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ProudNetSrc
{
  public class MaxUseLock
  {
    private readonly ConcurrentQueue<TaskCompletionSource<bool>> _queue =
        new ConcurrentQueue<TaskCompletionSource<bool>>();

    private int MaxUse { get; }

    private int InUse => _queue.Count;

    public MaxUseLock(int max)
    {
      MaxUse = max;
    }

    public IDisposable Lock()
    {
      if (InUse >= MaxUse)
      {
        _queue.TryPeek(out var src);
        src.Task.Wait();
      }

      _queue.Enqueue(new TaskCompletionSource<bool>());
      return new DisposableObject(this);
    }

    public async Task<IDisposable> LockAsync()
    {
      if (InUse >= MaxUse)
      {
        _queue.TryPeek(out var src);
        await src.Task;
      }

      _queue.Enqueue(new TaskCompletionSource<bool>());
      return new DisposableObject(this);
    }

    private void ReleaseNext()
    {
      _queue.TryDequeue(out var src);
      src.SetResult(true);
    }

    private class DisposableObject : IDisposable
    {
      private MaxUseLock _lock;

      public DisposableObject(MaxUseLock @lock)
      {
        _lock = @lock;
      }

      public void Dispose()
      {
        _lock.ReleaseNext();
        _lock = null;
      }
    }
  }
}
