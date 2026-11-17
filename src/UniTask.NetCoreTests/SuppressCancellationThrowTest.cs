using Cysharp.Threading.Tasks;
using System;
using System.Threading.Tasks;
using Xunit;

namespace NetCoreTests
{
    public partial class AutoResetUniTaskCompletionSourceTest
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public async Task ConsumesSource(bool generic, bool canceled, bool completedBeforeSuppression)
        {
            var source = new CountingSource();
            if (completedBeforeSuppression) source.Complete(canceled);

            if (generic)
            {
                var task = new UniTask<int>(source, 0).SuppressCancellationThrow();
                if (!completedBeforeSuppression) source.Complete(canceled);
                var result = await task;
                Assert.Equal(canceled, result.IsCanceled);
                Assert.Equal(canceled ? 0 : 42, result.Result);
            }
            else
            {
                var task = new UniTask(source, 0).SuppressCancellationThrow();
                if (!completedBeforeSuppression) source.Complete(canceled);
                Assert.Equal(canceled, await task);
            }

            Assert.Equal(1, source.GetResultCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreservesFault(bool generic)
        {
            var source = new CountingSource();
            var error = new InvalidOperationException("expected fault");
            source.Fail(error);
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                if (generic) await new UniTask<int>(source, 0).SuppressCancellationThrow();
                else await new UniTask(source, 0).SuppressCancellationThrow();
            });
            Assert.Same(error, actual);
            Assert.Equal(1, source.GetResultCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReturnsAutoResetSourceToPool(bool canceled)
        {
            var source = AutoResetUniTaskCompletionSource.Create();
            var task = source.Task;
            if (canceled) source.TrySetCanceled();
            else source.TrySetResult();
            Assert.Equal(canceled, await task.SuppressCancellationThrow());
            // Returning to the pool resets the core and invalidates the old token.
            Assert.Throws<InvalidOperationException>(() => task.Status);
        }

        [Fact]
        public async Task HandlesSourceFreeTasks()
        {
            Assert.False(await UniTask.CompletedTask.SuppressCancellationThrow());
            var result = await UniTask.FromResult(42).SuppressCancellationThrow();
            Assert.False(result.IsCanceled);
            Assert.Equal(42, result.Result);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ConsumesSourceAfterContinuationRegistration(bool generic, bool canceled)
        {
            var source = new CountingSource();
            async Task Consume()
            {
                if (generic)
                {
                    var result = await new UniTask<int>(source, 0).SuppressCancellationThrow();
                    Assert.Equal(canceled, result.IsCanceled);
                    Assert.Equal(canceled ? 0 : 42, result.Result);
                }
                else
                {
                    Assert.Equal(canceled, await new UniTask(source, 0).SuppressCancellationThrow());
                }
            }
            var pending = Consume();
            Assert.False(pending.IsCompleted);
            source.Complete(canceled);
            await pending;
            Assert.Equal(1, source.GetResultCalls);
        }

        [Fact]
        public async Task ReturnsGenericCanceledAutoResetSourceToPool()
        {
            var source = AutoResetUniTaskCompletionSource<int>.Create();
            var task = source.Task;
            source.TrySetCanceled();
            var result = await task.SuppressCancellationThrow();
            Assert.True(result.IsCanceled);
            Assert.Equal(0, result.Result);
            Assert.Throws<InvalidOperationException>(() => task.Status);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreservesFaultAfterContinuationRegistration(bool generic)
        {
            var source = new CountingSource();
            var error = new InvalidOperationException("expected pending fault");
            async Task Consume()
            {
                if (generic) await new UniTask<int>(source, 0).SuppressCancellationThrow();
                else await new UniTask(source, 0).SuppressCancellationThrow();
            }
            var pending = Consume();
            Assert.False(pending.IsCompleted);
            source.Fail(error);
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            Assert.Same(error, actual);
            Assert.Equal(1, source.GetResultCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReturnsCanceledAutoResetSourceAfterContinuationRegistration(bool generic)
        {
            if (generic)
            {
                var source = AutoResetUniTaskCompletionSource<int>.Create();
                var task = source.Task;
                var pending = task.SuppressCancellationThrow().AsTask();
                Assert.False(pending.IsCompleted);
                source.TrySetCanceled();
                Assert.True((await pending).IsCanceled);
                Assert.Throws<InvalidOperationException>(() => task.Status);
            }
            else
            {
                var source = AutoResetUniTaskCompletionSource.Create();
                var task = source.Task;
                var pending = task.SuppressCancellationThrow().AsTask();
                Assert.False(pending.IsCompleted);
                source.TrySetCanceled();
                Assert.True(await pending);
                Assert.Throws<InvalidOperationException>(() => task.Status);
            }
        }

        sealed class CountingSource : IUniTaskSource<int>
        {
            UniTaskCompletionSourceCore<int> core;
            public int GetResultCalls { get; private set; }
            public void Complete(bool canceled)
            {
                if (canceled) core.TrySetCanceled();
                else core.TrySetResult(42);
            }
            public void Fail(Exception error) => core.TrySetException(error);
            public int GetResult(short token)
            {
                GetResultCalls++;
                return core.GetResult(token);
            }
            void IUniTaskSource.GetResult(short token) => GetResult(token);
            public UniTaskStatus GetStatus(short token) => core.GetStatus(token);
            public UniTaskStatus UnsafeGetStatus() => core.UnsafeGetStatus();
            public void OnCompleted(Action<object> continuation, object state, short token)
                => core.OnCompleted(continuation, state, token);
        }
    }
}
