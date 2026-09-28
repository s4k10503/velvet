using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    public static class VelvetTaskExtensions
    {
        public static void Forget(this VelvetTask task)
        {
            var awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                PublishUnobserved(task);
                return;
            }

            awaiter.OnCompleted(() => PublishUnobserved(task));
        }

        public static void Forget<T>(this VelvetTask<T> task) => ((VelvetTask)task).Forget();

        static void PublishUnobserved(VelvetTask task)
        {
            var faults = VelvetTaskOutcome.Consume(task).Faults;
            if (faults == null)
            {
                return;
            }

            foreach (var fault in faults)
            {
                VelvetTaskScheduler.PublishUnobservedException(fault.SourceException);
            }
        }

        public static VelvetTask AttachExternalCancellation(this VelvetTask task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || task.Status.IsCompleted())
            {
                return task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                task.Forget();
                return FromCanceled(cancellationToken);
            }

            return new VelvetTask(new AttachExternalCancellationVelvetTaskSource(task, cancellationToken));
        }

        public static VelvetTask<T> AttachExternalCancellation<T>(this VelvetTask<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || task.Status.IsCompleted())
            {
                return task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                task.Forget();
                return FromCanceled<T>(cancellationToken);
            }

            return new VelvetTask<T>(new AttachExternalCancellationVelvetTaskSource<T>(task, cancellationToken));
        }

        public static IEnumerator ToCoroutine(Func<VelvetTask> taskFactory)
        {
            return taskFactory().ToCoroutine();
        }

        public static IEnumerator ToCoroutine(this VelvetTask task, Action<Exception>? exceptionHandler = null)
        {
            return new ToCoroutineEnumerator(task, exceptionHandler);
        }

        public static IEnumerator ToCoroutine<T>(this VelvetTask<T> task, Action<T>? resultHandler = null, Action<Exception>? exceptionHandler = null)
        {
            return new ToCoroutineEnumerator<T>(task, resultHandler, exceptionHandler);
        }

        internal static VelvetTask FromCanceled(CancellationToken cancellationToken = default)
        {
            var source = VelvetTaskSourcePool.Rent();
            source.MarkReturnToPoolOnConsume();
            source.TrySetCanceled(cancellationToken);
            return new VelvetTask(source);
        }

        internal static VelvetTask<T> FromCanceled<T>(CancellationToken cancellationToken = default)
        {
            var source = VelvetTaskSourcePool<T>.Rent();
            source.MarkReturnToPoolOnConsume();
            source.TrySetCanceled(cancellationToken);
            return new VelvetTask<T>(source);
        }

        sealed class ToCoroutineEnumerator : IEnumerator
        {
            readonly VelvetTask _task;
            readonly Action<Exception>? _exceptionHandler;
            bool _started;
            bool _completed;
            ExceptionDispatchInfo? _exception;

            public ToCoroutineEnumerator(VelvetTask task, Action<Exception>? exceptionHandler)
            {
                _task = task;
                _exceptionHandler = exceptionHandler;
            }

            public object? Current => null;

            public bool MoveNext()
            {
                if (!_started)
                {
                    _started = true;
                    Run();
                }

                if (_exception != null)
                {
                    _exception.Throw();
                }

                return !_completed;
            }

            public void Reset()
            {
            }

            async void Run()
            {
                try
                {
                    await _task;
                }
                catch (Exception ex)
                {
                    if (_exceptionHandler != null)
                    {
                        _exceptionHandler(ex);
                    }
                    else
                    {
                        _exception = ExceptionDispatchInfo.Capture(ex);
                    }
                }
                finally
                {
                    _completed = true;
                }
            }
        }

        sealed class ToCoroutineEnumerator<T> : IEnumerator
        {
            readonly VelvetTask<T> _task;
            readonly Action<T>? _resultHandler;
            readonly Action<Exception>? _exceptionHandler;
            bool _started;
            bool _completed;
            object? _current;
            ExceptionDispatchInfo? _exception;

            public ToCoroutineEnumerator(VelvetTask<T> task, Action<T>? resultHandler, Action<Exception>? exceptionHandler)
            {
                _task = task;
                _resultHandler = resultHandler;
                _exceptionHandler = exceptionHandler;
            }

            public object? Current => _current;

            public bool MoveNext()
            {
                if (!_started)
                {
                    _started = true;
                    Run();
                }

                if (_exception != null)
                {
                    _exception.Throw();
                }

                return !_completed;
            }

            public void Reset()
            {
            }

            async void Run()
            {
                try
                {
                    var result = await _task;
                    _current = result;
                    _resultHandler?.Invoke(result);
                }
                catch (Exception ex)
                {
                    if (_exceptionHandler != null)
                    {
                        _exceptionHandler(ex);
                    }
                    else
                    {
                        _exception = ExceptionDispatchInfo.Capture(ex);
                    }
                }
                finally
                {
                    _completed = true;
                }
            }
        }
    }
}
