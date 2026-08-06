using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using PlayerLoopType = UnityEngine.PlayerLoop;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Velvet
{
    // EditMode batchmode does not tick the PlayerLoop, so frame-bound VelvetTask.Yield continuations
    // register on EditorApplication.update there. In Play Mode and player builds they register on the
    // PlayerLoop Update pass instead.
    internal static class VelvetTaskFrameDriver
    {
        const int InitialQueueCapacity = 64;

        static readonly List<YieldVelvetTaskSource> QueueA = new(InitialQueueCapacity);
        static readonly List<YieldVelvetTaskSource> QueueB = new(InitialQueueCapacity);
        static List<YieldVelvetTaskSource> _waitQueue = QueueA;
        static List<YieldVelvetTaskSource> _runQueue = QueueB;

#if UNITY_EDITOR
        static bool _editorUpdateHooked;
#endif
        static bool _playerLoopInitialized;

        internal static void Schedule(YieldVelvetTaskSource source)
        {
            _waitQueue.Add(source);
            EnsureScheduled();
        }

        internal static void Unschedule(YieldVelvetTaskSource source)
        {
            _waitQueue.Remove(source);
            _runQueue.Remove(source);
            MaybeUnhookEditorUpdate();
        }

        static void EnsureScheduled()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!_editorUpdateHooked)
                {
                    EditorApplication.update += OnEditorUpdate;
                    _editorUpdateHooked = true;
                }

                return;
            }
#endif
            EnsurePlayerLoop();
        }

        static void EnsurePlayerLoop()
        {
            if (_playerLoopInitialized)
            {
                return;
            }

            InsertPlayerLoopSystem();
            _playerLoopInitialized = true;
        }

        static void InsertPlayerLoopSystem()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            var subSystems = playerLoop.subSystemList;
            for (var i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type != typeof(PlayerLoopType.Update))
                {
                    continue;
                }

                ref var updateLoop = ref subSystems[i];
                var updateSubSystems = updateLoop.subSystemList;
                var extended = new PlayerLoopSystem[updateSubSystems.Length + 1];
                for (var j = 0; j < updateSubSystems.Length; j++)
                {
                    extended[j] = updateSubSystems[j];
                }

                extended[extended.Length - 1] = new PlayerLoopSystem
                {
                    type = typeof(VelvetTaskFrameDriver),
                    updateDelegate = OnPlayerLoopUpdate,
                };
                updateLoop.subSystemList = extended;
                playerLoop.subSystemList = subSystems;
                PlayerLoop.SetPlayerLoop(playerLoop);
                return;
            }
        }

#if UNITY_EDITOR
        static void MaybeUnhookEditorUpdate()
        {
            if (!_editorUpdateHooked || _waitQueue.Count > 0 || _runQueue.Count > 0)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            _editorUpdateHooked = false;
        }

        static void OnEditorUpdate() => DrainScheduledYields();
#endif

        static void OnPlayerLoopUpdate() => DrainScheduledYields();

        internal static void DrainScheduledYields()
        {
            if (_waitQueue.Count == 0)
            {
#if UNITY_EDITOR
                MaybeUnhookEditorUpdate();
#endif
                return;
            }

            (_waitQueue, _runQueue) = (_runQueue, _waitQueue);
            for (var i = 0; i < _runQueue.Count; i++)
            {
                _runQueue[i].CompleteScheduledFrame();
            }

            _runQueue.Clear();
#if UNITY_EDITOR
            MaybeUnhookEditorUpdate();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            QueueA.Clear();
            QueueB.Clear();
            _waitQueue = QueueA;
            _runQueue = QueueB;
#if UNITY_EDITOR
            if (_editorUpdateHooked)
            {
                EditorApplication.update -= OnEditorUpdate;
                _editorUpdateHooked = false;
            }
#endif
            _playerLoopInitialized = false;
        }
    }
}
