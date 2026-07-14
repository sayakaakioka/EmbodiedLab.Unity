using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace UnityEngine
{
    [AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder))]
    public readonly struct Awaitable
    {
        private readonly Task task;

        internal Awaitable(Task task)
        {
            this.task = task;
        }

        public TaskAwaiter GetAwaiter() => task.GetAwaiter();
    }

    [AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder<>))]
    public readonly struct Awaitable<T>
    {
        private readonly Task<T> task;

        internal Awaitable(Task<T> task)
        {
            this.task = task;
        }

        public TaskAwaiter<T> GetAwaiter() => task.GetAwaiter();
    }

    public struct AwaitableAsyncMethodBuilder
    {
        private AsyncTaskMethodBuilder builder;

        public static AwaitableAsyncMethodBuilder Create() => new()
        {
            builder = AsyncTaskMethodBuilder.Create(),
        };

        public Awaitable Task => new(builder.Task);

        public void SetResult() => builder.SetResult();

        public void SetException(Exception exception) => builder.SetException(exception);

        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            builder.SetStateMachine(stateMachine);

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => builder.Start(ref stateMachine);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            builder.AwaitOnCompleted(ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public struct AwaitableAsyncMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;

        public static AwaitableAsyncMethodBuilder<T> Create() => new()
        {
            builder = AsyncTaskMethodBuilder<T>.Create(),
        };

        public Awaitable<T> Task => new(builder.Task);

        public void SetResult(T result) => builder.SetResult(result);

        public void SetException(Exception exception) => builder.SetException(exception);

        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            builder.SetStateMachine(stateMachine);

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => builder.Start(ref stateMachine);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            builder.AwaitOnCompleted(ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}
