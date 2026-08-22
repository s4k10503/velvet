using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Velvet.TestUtilities;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskStructStateMachineContinuationTests
    {
        // MoveNext makes no Builder.SetStateMachine call. That call boxes this struct after
        // AwaitOnCompleted has populated its builder, so Run would resume the box instead of the copy
        // Rent took, and an unset runner on that copy would never show.
        struct TwoYieldStructStateMachine : IAsyncStateMachine
        {
            public int State;
            public VelvetTaskMethodBuilder<int> Builder;
            public int Token;
            VelvetTask.Awaiter _awaiter1;
            VelvetTask.Awaiter _awaiter2;

            public void MoveNext()
            {
                var state = State;
                try
                {
                    if (state == -1)
                    {
                        Token = 17;
                        _awaiter1 = VelvetTask.Yield().GetAwaiter();
                        if (!_awaiter1.IsCompleted)
                        {
                            State = 0;
                            Builder.AwaitOnCompleted(ref _awaiter1, ref this);
                            return;
                        }
                    }
                    else if (state == 0)
                    {
                        _awaiter1.GetResult();
                    }
                    else if (state == 1)
                    {
                        _awaiter2.GetResult();
                        State = -2;
                        Builder.SetResult(Token);
                        return;
                    }
                    else
                    {
                        return;
                    }

                    State = -1;
                    Token += 5;
                    _awaiter2 = VelvetTask.Yield().GetAwaiter();
                    if (!_awaiter2.IsCompleted)
                    {
                        State = 1;
                        Builder.AwaitOnCompleted(ref _awaiter2, ref this);
                        return;
                    }

                    _awaiter2.GetResult();
                    State = -2;
                    Builder.SetResult(Token);
                }
                catch (Exception ex)
                {
                    State = -2;
                    Builder.SetException(ex);
                }
            }

            public void SetStateMachine(IAsyncStateMachine stateMachine) =>
                Builder.SetStateMachine(stateMachine);
        }

        // Reaching either AwaitUnsafeOnCompleted overload takes an awaiter of the fixture's own: both
        // awaiters Velvet declares implement INotifyCompletion alone, so neither satisfies the
        // ICriticalNotifyCompletion those overloads constrain their awaiter to.
        sealed class ContinuationGate
        {
            Action _continuation;

            public bool IsCompleted { get; private set; }

            public void Register(Action continuation) => _continuation = continuation;

            public void Complete()
            {
                IsCompleted = true;
                var continuation = _continuation;
                _continuation = null;
                continuation?.Invoke();
            }
        }

        readonly struct ContinuationGateAwaiter : ICriticalNotifyCompletion
        {
            readonly ContinuationGate _gate;

            public ContinuationGateAwaiter(ContinuationGate gate) => _gate = gate;

            public bool IsCompleted => _gate.IsCompleted;

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation) => _gate.Register(continuation);

            public void UnsafeOnCompleted(Action continuation) => _gate.Register(continuation);
        }

        // Same no-SetStateMachine constraint as TwoYieldStructStateMachine.
        struct YieldingVoidStructStateMachine : IAsyncStateMachine
        {
            public int State;
            public VelvetTaskMethodBuilder Builder;
            VelvetTask.Awaiter _awaiter;

            public void MoveNext()
            {
                try
                {
                    if (State == -1)
                    {
                        _awaiter = VelvetTask.Yield().GetAwaiter();
                        if (!_awaiter.IsCompleted)
                        {
                            State = 0;
                            Builder.AwaitOnCompleted(ref _awaiter, ref this);
                            return;
                        }
                    }
                    else if (State != 0)
                    {
                        return;
                    }

                    _awaiter.GetResult();
                    State = -2;
                    Builder.SetResult();
                }
                catch (Exception ex)
                {
                    State = -2;
                    Builder.SetException(ex);
                }
            }

            public void SetStateMachine(IAsyncStateMachine stateMachine) =>
                Builder.SetStateMachine(stateMachine);
        }

        // Same no-SetStateMachine constraint as TwoYieldStructStateMachine.
        struct GatedVoidStructStateMachine : IAsyncStateMachine
        {
            public int State;
            public VelvetTaskMethodBuilder Builder;
            public ContinuationGate Gate;
            ContinuationGateAwaiter _awaiter;

            public void MoveNext()
            {
                try
                {
                    if (State == -1)
                    {
                        _awaiter = new ContinuationGateAwaiter(Gate);
                        if (!_awaiter.IsCompleted)
                        {
                            State = 0;
                            Builder.AwaitUnsafeOnCompleted(ref _awaiter, ref this);
                            return;
                        }
                    }
                    else if (State != 0)
                    {
                        return;
                    }

                    _awaiter.GetResult();
                    State = -2;
                    Builder.SetResult();
                }
                catch (Exception ex)
                {
                    State = -2;
                    Builder.SetException(ex);
                }
            }

            public void SetStateMachine(IAsyncStateMachine stateMachine) =>
                Builder.SetStateMachine(stateMachine);
        }

        // Same no-SetStateMachine constraint as TwoYieldStructStateMachine.
        struct GatedValueStructStateMachine : IAsyncStateMachine
        {
            public int State;
            public VelvetTaskMethodBuilder<int> Builder;
            public ContinuationGate Gate;
            public int Token;
            ContinuationGateAwaiter _awaiter;

            public void MoveNext()
            {
                try
                {
                    if (State == -1)
                    {
                        Token = 17;
                        _awaiter = new ContinuationGateAwaiter(Gate);
                        if (!_awaiter.IsCompleted)
                        {
                            State = 0;
                            Builder.AwaitUnsafeOnCompleted(ref _awaiter, ref this);
                            return;
                        }
                    }
                    else if (State != 0)
                    {
                        return;
                    }

                    _awaiter.GetResult();
                    Token += 5;
                    State = -2;
                    Builder.SetResult(Token);
                }
                catch (Exception ex)
                {
                    State = -2;
                    Builder.SetException(ex);
                }
            }

            public void SetStateMachine(IAsyncStateMachine stateMachine) =>
                Builder.SetStateMachine(stateMachine);
        }

        static async VelvetTask<int> AccumulateAcrossTwoYields()
        {
            var token = 17;
            await VelvetTask.Yield();
            token += 5;
            await VelvetTask.Yield();
            return token;
        }

        [Test]
        public void Given_EditorCompiledAsyncMethod_When_StateMachineReflected_Then_IsClassRatherThanStruct()
        {
            // Arrange
            var stateMachineType = Array.Find(
                typeof(VelvetTaskStructStateMachineContinuationTests).GetNestedTypes(
                    System.Reflection.BindingFlags.NonPublic),
                t => t.Name.StartsWith("<AccumulateAcrossTwoYields>d__", StringComparison.Ordinal));
            Assume.That(stateMachineType, Is.Not.Null);

            // Act
            var isValueType = stateMachineType!.IsValueType;

            // Assert — a class state machine is shared with the runner by reference, so a builder field
            // written after Rent's copy still reaches it. The value-type path where that copy loses the
            // write is reached here only through the hand-rolled TwoYieldStructStateMachine.
            Assert.That(isValueType, Is.False);
        }

        [Test]
        public void Given_StructStateMachineWithTwoYields_When_EditorUpdateDrained_Then_PreservesLocalsAcrossSuspensions()
        {
            // Arrange
            var stateMachine = new TwoYieldStructStateMachine
            {
                Builder = VelvetTaskMethodBuilder<int>.Create(),
                State = -1,
            };
            stateMachine.Builder.Start(ref stateMachine);
            Assume.That(stateMachine.Builder.Task.Status.IsCompleted(), Is.False);

            // Act
            DrainEditorUpdateForTest();
            DrainEditorUpdateForTest();
            var result = stateMachine.Builder.Task.GetAwaiter().GetResult();

            // Assert
            Assert.That(result, Is.EqualTo(22));
        }

        [Test]
        public void Given_StructStateMachineOnTheVoidBuilder_When_EditorUpdateDrained_Then_CompletesTheTaskTheCallerHolds()
        {
            // Arrange
            var stateMachine = new YieldingVoidStructStateMachine
            {
                Builder = VelvetTaskMethodBuilder.Create(),
                State = -1,
            };
            stateMachine.Builder.Start(ref stateMachine);
            var pendingBeforeDrain = !stateMachine.Builder.Task.Status.IsCompleted();

            // Act
            DrainEditorUpdateForTest();
            var status = stateMachine.Builder.Task.Status;

            // Assert
            Assert.That((pendingBeforeDrain, status), Is.EqualTo((true, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_StructStateMachineOnTheVoidBuilderSuspendedOnACriticalAwaiter_When_ThatAwaiterCompletes_Then_CompletesTheTaskTheCallerHolds()
        {
            // Arrange
            var gate = new ContinuationGate();
            var stateMachine = new GatedVoidStructStateMachine
            {
                Builder = VelvetTaskMethodBuilder.Create(),
                Gate = gate,
                State = -1,
            };
            stateMachine.Builder.Start(ref stateMachine);
            var pendingBeforeCompletion = !stateMachine.Builder.Task.Status.IsCompleted();

            // Act
            gate.Complete();
            var status = stateMachine.Builder.Task.Status;

            // Assert
            Assert.That((pendingBeforeCompletion, status), Is.EqualTo((true, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_StructStateMachineOnTheValueBuilderSuspendedOnACriticalAwaiter_When_ThatAwaiterCompletes_Then_DeliversTheResultToTheCaller()
        {
            // Arrange
            var gate = new ContinuationGate();
            var stateMachine = new GatedValueStructStateMachine
            {
                Builder = VelvetTaskMethodBuilder<int>.Create(),
                Gate = gate,
                State = -1,
            };
            stateMachine.Builder.Start(ref stateMachine);
            var pendingBeforeCompletion = !stateMachine.Builder.Task.Status.IsCompleted();

            // Act
            gate.Complete();
            var result = stateMachine.Builder.Task.GetAwaiter().GetResult();

            // Assert
            Assert.That((pendingBeforeCompletion, result), Is.EqualTo((true, 22)));
        }

        [Test]
        public void Given_CompiledAsyncMethodWithTwoYields_When_EditorUpdateDrained_Then_PreservesLocalsAcrossSuspensions()
        {
            // Arrange
            var task = AccumulateAcrossTwoYields();
            Assume.That(task.Status.IsCompleted(), Is.False);

            // Act
            DrainEditorUpdateForTest();
            DrainEditorUpdateForTest();
            var result = task.GetAwaiter().GetResult();

            // Assert
            Assert.That(result, Is.EqualTo(22));
        }
    }
}
