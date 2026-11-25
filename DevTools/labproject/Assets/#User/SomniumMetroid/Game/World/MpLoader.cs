using System;
using System.Collections.Generic;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// One background thread for disc reads and parsing (MREA, CMDL, TXTR decode). Jobs run in order; each hands its result back to
    /// the main thread through <see cref="Drain"/> (called by MpGame every frame), where Unity objects are made. The thread uses a
    /// 64 MB stack at most (the BO2 Dreamport measured Mono thread cost growing with the stack size).
    /// </summary>
    public sealed class MpLoader : IDisposable
    {
        private sealed class Job
        {
            public Func<object> Work;
            public Action<object, Exception> Done;
            public object Result;
            public Exception Error;
        }

        private readonly Queue<Job> _todo = new Queue<Job>();
        private readonly Queue<Job> _done = new Queue<Job>();
        private readonly object _lock = new object();
        private readonly Thread _thread;
        private volatile bool _stop;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        public int Pending { get { lock (_lock) return _todo.Count + (_busy ? 1 : 0); } }
        private bool _busy;

        public MpLoader()
        {
            _thread = new Thread(Run, 16 << 20) { IsBackground = true, Name = "Metroid loader" };
            _thread.Start();
        }

        public void Add(Func<object> work, Action<object, Exception> done)
        {
            lock (_lock) _todo.Enqueue(new Job { Work = work, Done = done });
            _wake.Set();
        }

        private void Run()
        {
            while (!_stop)
            {
                Job j = null;
                lock (_lock)
                {
                    if (_todo.Count > 0)
                    {
                        j = _todo.Dequeue();
                        _busy = true;
                    }
                }

                if (j == null)
                {
                    _wake.WaitOne(100);
                    continue;
                }

                try { j.Result = j.Work(); }
                catch (Exception e) { j.Error = e; }
                lock (_lock)
                {
                    _done.Enqueue(j);
                    _busy = false;
                }
            }
        }

        /// <summary>Main thread: runs the finished jobs' callbacks (at most <paramref name="max"/>).</summary>
        public void Drain(int max)
        {
            for (int i = 0; i < max; i++)
            {
                Job j;
                lock (_lock)
                {
                    if (_done.Count == 0) return;
                    j = _done.Dequeue();
                }

                try { j.Done(j.Result, j.Error); }
                catch (Exception e) { MpLog.Error("Loader: a finished job failed on the main thread: " + e); }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
        }
    }
}
