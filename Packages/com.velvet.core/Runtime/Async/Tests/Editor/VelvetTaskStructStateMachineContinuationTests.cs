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
                            Builder.SetStateMachine(this);
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
                        Builder.SetStateMachine(this);
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

        static async VelvetTask<int> AccumulateAcrossTwoYields()
        {
            var token = 17;
            await VelvetTask.Yield();
            token += 5;
            await VelvetTask.Yield();
            return token;
        }

        [Test]
        public void Given_CompiledAsyncStateMachineForAccumulateAcrossTwoYields_When_Reflected_Then_IsValueTypeMatchesPinnedExpectation()
        {
            // Arrange
            var stateMachineType = Array.Find(
                typeof(VelvetTaskStructStateMachineContinuationTests).GetNestedTypes(
                    System.Reflection.BindingFlags.NonPublic),
                t => t.Name.StartsWith("<AccumulateAcrossTwoYields>d__", StringComparison.Ordinal));
            Assume.That(stateMachineType, Is.Not.Null);

            // Act
            var isValueType = stateMachineType!.IsValueType;

            // Assert — editor script compilation emits class state machines for this fixture.
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
