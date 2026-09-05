using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 시간 기반 콜백 스케줄러입니다. Unity 의 Update 사이클 위에서 동작합니다.
    /// RegisterInterval 은 일정 간격마다, RegisterOverDuration 은 지정한 총 시간 동안 균등하게,
    /// ScheduleOnce 는 지연 후 한 번만 콜백을 호출합니다.
    /// </summary>
    /// <remarks>
    /// 등록 인자가 잘못되면 예외를 던지지 않고 로그를 남긴 뒤 <see cref="ScheduleHandle.None"/> 을 반환합니다.
    /// 스케줄 등록은 대부분 게임 로직 도중에 일어나므로, 인자 하나 때문에 그 프레임 전체가 중단되지 않게 하기 위함입니다.
    /// </remarks>
    public class Scheduler : MonoBehaviour
    {
        #region Nested Types

        /// <summary>Tick 콜백에 전달되는 진행 정보입니다.</summary>
        public readonly struct TickInfo
        {
            /// <summary>이번이 몇 번째 호출인지. 0부터 시작합니다.</summary>
            public readonly int IterationIndex;

            /// <summary>남은 호출 횟수. 무한 반복은 -1 입니다.</summary>
            public readonly int RemainingCount;

            /// <summary>마지막 호출인지 여부. 무한 반복은 항상 false 입니다.</summary>
            public readonly bool IsLastIteration;

            /// <summary>스케줄 ID.</summary>
            public readonly long ScheduleId;

            internal TickInfo(int iterationIndex, int remainingCount, bool isLastIteration, long scheduleId)
            {
                IterationIndex = iterationIndex;
                RemainingCount = remainingCount;
                IsLastIteration = isLastIteration;
                ScheduleId = scheduleId;
            }
        }

        //== 등록된 스케줄 한 건. 풀에서 재사용하므로 Reset 이 모든 필드를 빠짐없이 덮어써야 합니다.
        private sealed class Schedule
        {
            public const int InfiniteLoop = -1;

            public long Id;
            public Action<TickInfo> TickHandler;
            public Action CompletedHandler;
            public float Interval;
            public int LoopCount;
            public bool IsRealTime;

            public float ElapsedTime;
            public int IterationIndex;
            public bool IsCanceled;
            public bool IsPaused;

            //== fireImmediately 로 등록된 경우 첫 Update 에서 한 번 즉시 발화시키기 위한 플래그입니다.
            public bool PendingImmediateFire;

            //== RegisterXxxFor 로 등록된 경우에만 true. Owner 가 파괴되면 스케줄도 정리합니다.
            public bool TrackOwner;
            public MonoBehaviour Owner;

            public bool IsInfinite
            {
                get { return LoopCount == InfiniteLoop; }
            }

            public void Reset(
                long id,
                Action<TickInfo> tickHandler,
                Action completedHandler,
                float interval,
                int loopCount,
                bool isRealTime,
                bool fireImmediately,
                MonoBehaviour owner,
                bool trackOwner)
            {
                Id = id;
                TickHandler = tickHandler;
                CompletedHandler = completedHandler;
                Interval = interval;
                LoopCount = loopCount;
                IsRealTime = isRealTime;

                ElapsedTime = 0f;
                IterationIndex = 0;
                IsCanceled = false;
                IsPaused = false;
                PendingImmediateFire = fireImmediately;

                Owner = owner;
                TrackOwner = trackOwner;
            }
        }

        #endregion

        #region Constants

        //== 프레임이 크게 밀렸을 때 밀린 만큼을 한 프레임에 몰아 호출하면 그 프레임이 또 밀립니다.
        //== 정확한 호출 횟수보다 프레임 안정성을 택해 상한을 둡니다.
        private const int MaxTicksPerFrame = 64;

        #endregion

        #region Static

        private static Scheduler _defaultInstance;

        /// <summary>
        /// 라이브러리가 제공하는 공용 인스턴스. 첫 접근 시 DontDestroyOnLoad GameObject 를 만듭니다.
        /// </summary>
        public static Scheduler Default
        {
            get
            {
                if (_defaultInstance != null)
                {
                    return _defaultInstance;
                }

                GameObject host = new GameObject("[Cocoa.Lib] Scheduler");
                DontDestroyOnLoad(host);
                _defaultInstance = host.AddComponent<Scheduler>();

                return _defaultInstance;
            }
        }

        #endregion

        #region Fields

        //== Update 순회용. 인덱스 순회가 필요해 리스트로 둡니다.
        private readonly List<Schedule> _schedules = new List<Schedule>();

        //== ID 조회용. 순회 리스트와 같은 인스턴스를 가리킵니다.
        private readonly Dictionary<long, Schedule> _scheduleById = new Dictionary<long, Schedule>();

        //== 해제된 항목 재사용 풀. 스케줄 등록이 잦은 구간에서 매번 새로 할당하지 않게 합니다.
        private readonly Queue<Schedule> _schedulePool = new Queue<Schedule>();

        //== 마지막으로 발급한 스케줄 ID. 0 은 빈 핸들용으로 예약되어 있어 첫 발급은 1 입니다.
        private long _lastScheduleId;

        //== 전역 일시정지 상태. 게이트에서 매번 확인하므로 이 값을 켜두면 새로 등록되는 스케줄도 정지 상태로 시작합니다.
        [SerializeField] private bool _allPaused;

        //== 인스펙터 확인용. ActiveCount 와 같은 값이며 값을 쓰지는 않습니다.
        [SerializeField, ReadOnly] private int _activeScheduleCount;

        #endregion

        #region Properties

        /// <summary>추적 중인 스케줄 수. 일시정지된 것도 포함합니다.</summary>
        public int ActiveCount
        {
            get { return _schedules.Count; }
        }

        /// <summary>전역 일시정지 상태인지 여부.</summary>
        public bool IsPausedAll
        {
            get { return _allPaused; }
        }

        #endregion

        #region Public API - RegisterInterval

        /// <summary>
        /// 일정 간격마다 콜백을 호출하는 스케줄을 등록합니다.
        /// </summary>
        /// <param name="delay">호출 간격(초). 0 이면 매 프레임 호출합니다.</param>
        /// <param name="count">총 호출 횟수. -1 이면 무한 반복입니다.</param>
        /// <param name="onTick">매 호출마다 실행할 콜백.</param>
        /// <param name="onCompleted">지정 횟수를 모두 채웠을 때 호출할 콜백. 무한 반복이나 취소 시에는 호출되지 않습니다.</param>
        /// <param name="isRealTime">true 면 unscaledDeltaTime 을 사용하고 PauseAll 에서 면제됩니다.</param>
        /// <param name="fireImmediately">true 면 첫 호출이 즉시 일어나고 그 다음부터 delay 간격으로 호출됩니다.</param>
        /// <remarks>
        /// delay 1.0, count 5 기준으로 fireImmediately 가 false 면 1.0 2.0 3.0 4.0 5.0 초에,
        /// true 면 0 1.0 2.0 3.0 4.0 초에 호출됩니다. 총 호출 횟수는 두 경우 모두 같습니다.
        /// </remarks>
        public ScheduleHandle RegisterInterval(
            float delay,
            int count,
            Action<TickInfo> onTick,
            Action onCompleted = null,
            bool isRealTime = false,
            bool fireImmediately = false)
        {
            return RegisterIntervalInternal(null, false, delay, count, onTick, onCompleted, isRealTime, fireImmediately);
        }

        /// <summary>
        /// owner 의 수명에 묶인 Interval 스케줄입니다. owner 가 파괴되면 다음 Update 에서 취소됩니다.
        /// </summary>
        public ScheduleHandle RegisterIntervalFor(
            MonoBehaviour owner,
            float delay,
            int count,
            Action<TickInfo> onTick,
            Action onCompleted = null,
            bool isRealTime = false,
            bool fireImmediately = false)
        {
            if (owner == null)
            {
                Log.Error("[Scheduler] RegisterIntervalFor: owner 가 null 입니다.", LogColor.Red);
                return ScheduleHandle.None;
            }

            return RegisterIntervalInternal(owner, true, delay, count, onTick, onCompleted, isRealTime, fireImmediately);
        }

        #endregion

        #region Public API - RegisterOverDuration

        /// <summary>
        /// 지정한 총 시간 동안 count 회를 균등한 간격으로 호출하는 스케줄을 등록합니다.
        /// </summary>
        /// <param name="duration">총 시간(초).</param>
        /// <param name="count">총 호출 횟수. 1 이상이어야 하며 무한 반복은 지원하지 않습니다.</param>
        /// <param name="onTick">매 호출마다 실행할 콜백.</param>
        /// <param name="onCompleted">지정 횟수를 모두 채웠을 때 호출할 콜백.</param>
        /// <param name="isRealTime">true 면 unscaledDeltaTime 을 사용하고 PauseAll 에서 면제됩니다.</param>
        /// <param name="fireImmediately">true 면 첫 호출이 0초에 일어납니다. count 가 2 이상이어야 합니다.</param>
        /// <remarks>
        /// duration 1.0, count 5 기준으로 fireImmediately 가 false 면 0.2 간격으로 0.2 부터 1.0 까지,
        /// true 면 0.25 간격으로 0 부터 1.0 까지 호출됩니다. 두 경우 모두 마지막 호출이 duration 시점에 일어납니다.
        /// </remarks>
        public ScheduleHandle RegisterOverDuration(
            float duration,
            int count,
            Action<TickInfo> onTick,
            Action onCompleted = null,
            bool isRealTime = false,
            bool fireImmediately = false)
        {
            return RegisterOverDurationInternal(null, false, duration, count, onTick, onCompleted, isRealTime, fireImmediately);
        }

        /// <summary>
        /// owner 의 수명에 묶인 Duration 스케줄입니다. owner 가 파괴되면 다음 Update 에서 취소됩니다.
        /// </summary>
        public ScheduleHandle RegisterOverDurationFor(
            MonoBehaviour owner,
            float duration,
            int count,
            Action<TickInfo> onTick,
            Action onCompleted = null,
            bool isRealTime = false,
            bool fireImmediately = false)
        {
            if (owner == null)
            {
                Log.Error("[Scheduler] RegisterOverDurationFor: owner 가 null 입니다.", LogColor.Red);
                return ScheduleHandle.None;
            }

            return RegisterOverDurationInternal(owner, true, duration, count, onTick, onCompleted, isRealTime, fireImmediately);
        }

        #endregion

        #region Public API - ScheduleOnce

        /// <summary>
        /// 지정한 지연 후 한 번만 콜백을 호출합니다. 호출 전이라면 Unregister 로 취소할 수 있습니다.
        /// </summary>
        /// <param name="delay">지연 시간(초).</param>
        /// <param name="onComplete">delay 후 호출할 콜백.</param>
        /// <param name="isRealTime">true 면 unscaledDeltaTime 을 사용하고 PauseAll 에서 면제됩니다.</param>
        public ScheduleHandle ScheduleOnce(float delay, Action onComplete, bool isRealTime = false)
        {
            return ScheduleOnceInternal(null, false, delay, onComplete, isRealTime);
        }

        /// <summary>
        /// owner 의 수명에 묶인 1회 스케줄입니다. owner 가 파괴되면 콜백 없이 취소됩니다.
        /// </summary>
        public ScheduleHandle ScheduleOnceFor(MonoBehaviour owner, float delay, Action onComplete, bool isRealTime = false)
        {
            if (owner == null)
            {
                Log.Error("[Scheduler] ScheduleOnceFor: owner 가 null 입니다.", LogColor.Red);
                return ScheduleHandle.None;
            }

            return ScheduleOnceInternal(owner, true, delay, onComplete, isRealTime);
        }

        #endregion

        #region Public API - Pause

        /// <summary>지정한 스케줄을 일시정지합니다. realtime 스케줄도 이 호출로는 멈춥니다.</summary>
        public void Pause(long id)
        {
            if (_scheduleById.TryGetValue(id, out Schedule schedule))
            {
                schedule.IsPaused = true;
            }
        }

        /// <summary>
        /// 전역 일시정지 상태로 진입합니다. realtime 스케줄은 면제되어 계속 진행됩니다.
        /// 개별 플래그는 건드리지 않습니다. 게이트가 이 값을 직접 확인하므로 이후 등록되는 스케줄도 정지 상태로 시작합니다.
        /// </summary>
        public void PauseAll()
        {
            _allPaused = true;
        }

        #endregion

        #region Public API - Resume

        /// <summary>지정한 스케줄의 개별 일시정지를 해제합니다. 전역 일시정지 중이면 그대로 멈춰 있습니다.</summary>
        public void Resume(long id)
        {
            if (_scheduleById.TryGetValue(id, out Schedule schedule))
            {
                schedule.IsPaused = false;
            }
        }

        /// <summary>
        /// 전역 일시정지 상태를 해제합니다.
        /// 개별 Pause 로 멈춰둔 스케줄은 계속 멈춰 있습니다. 전역 정지가 개별 의도를 덮어쓰지 않게 하기 위함입니다.
        /// </summary>
        public void ResumeAll()
        {
            _allPaused = false;
        }

        #endregion

        #region Public API - Unregister

        /// <summary>
        /// 등록된 스케줄을 취소합니다. 실제 제거는 다음 Update 에서 일어나며 완료 콜백은 호출되지 않습니다.
        /// </summary>
        /// <returns>해당 ID 의 스케줄이 있어 취소되었는지 여부.</returns>
        public bool Unregister(long id)
        {
            if (!_scheduleById.TryGetValue(id, out Schedule schedule))
            {
                return false;
            }

            schedule.IsCanceled = true;
            return true;
        }

        /// <summary>등록된 모든 스케줄을 취소합니다.</summary>
        public void UnregisterAll()
        {
            for (int i = 0; i < _schedules.Count; i++)
            {
                _schedules[i].IsCanceled = true;
            }
        }

        #endregion

        #region Public API - Queries

        /// <summary>해당 ID 의 스케줄이 아직 살아있는지 여부. 취소 표시된 스케줄은 false 입니다.</summary>
        public bool IsActive(long id)
        {
            return _scheduleById.TryGetValue(id, out Schedule schedule) && !schedule.IsCanceled;
        }

        #endregion

        #region Unity Messages

        private void Update()
        {
            //== 프레임 시작 시점의 개수만 처리합니다. 콜백 안에서 새로 등록된 스케줄은 다음 프레임부터 진행합니다.
            int count = _schedules.Count;
            for (int i = 0; i < count; i++)
            {
                Schedule schedule = _schedules[i];

                if (schedule.IsCanceled)
                {
                    continue;
                }

                //== owner 가 사라진 스케줄은 완료 콜백 없이 정리합니다.
                if (schedule.TrackOwner && schedule.Owner == null)
                {
                    schedule.IsCanceled = true;
                    continue;
                }

                //== 일시정지 게이트. realtime 은 전역 정지에서 면제되지만 개별 Pause 는 그대로 적용됩니다.
                if (schedule.IsPaused || (_allPaused && !schedule.IsRealTime))
                {
                    continue;
                }

                if (schedule.PendingImmediateFire)
                {
                    schedule.PendingImmediateFire = false;
                    InvokeTick(schedule);

                    if (TryComplete(schedule))
                    {
                        continue;
                    }
                }

                if (schedule.IsRealTime)
                {
                    schedule.ElapsedTime += Time.unscaledDeltaTime;
                }
                else
                {
                    schedule.ElapsedTime += Time.deltaTime;
                }

                AdvanceTicks(schedule);
            }

            //== 취소된 항목 정리. 이번 프레임에 새로 등록된 것까지 포함해 전체를 훑습니다.
            for (int i = _schedules.Count - 1; i >= 0; i--)
            {
                if (_schedules[i].IsCanceled)
                {
                    RemoveAt(i);
                }
            }

            _activeScheduleCount = _schedules.Count;
        }

        private void OnDestroy()
        {
            //== 공용 인스턴스가 파괴되면 정적 참조를 끊어 다음 접근에서 새로 만들게 합니다.
            if (_defaultInstance == this)
            {
                _defaultInstance = null;
            }
        }

        #endregion

        #region Private Helpers - Registration

        private ScheduleHandle RegisterIntervalInternal(
            MonoBehaviour owner,
            bool trackOwner,
            float delay,
            int count,
            Action<TickInfo> onTick,
            Action onCompleted,
            bool isRealTime,
            bool fireImmediately)
        {
            if (delay < 0f)
            {
                Log.Error($"[Scheduler] RegisterInterval: delay 는 음수일 수 없습니다. (delay: {delay})", LogColor.Red);
                return ScheduleHandle.None;
            }

            if (count == 0 || count < Schedule.InfiniteLoop)
            {
                Log.Error($"[Scheduler] RegisterInterval: count 는 1 이상이거나 무한 반복을 뜻하는 -1 이어야 합니다. (count: {count})", LogColor.Red);
                return ScheduleHandle.None;
            }

            if (onTick == null)
            {
                Log.Error("[Scheduler] RegisterInterval: onTick 이 null 입니다.", LogColor.Red);
                return ScheduleHandle.None;
            }

            long id = Register(delay, count, onTick, onCompleted, isRealTime, fireImmediately, owner, trackOwner);
            return new ScheduleHandle(this, id);
        }

        private ScheduleHandle RegisterOverDurationInternal(
            MonoBehaviour owner,
            bool trackOwner,
            float duration,
            int count,
            Action<TickInfo> onTick,
            Action onCompleted,
            bool isRealTime,
            bool fireImmediately)
        {
            if (duration <= 0f)
            {
                Log.Error($"[Scheduler] RegisterOverDuration: duration 은 0보다 커야 합니다. (duration: {duration})", LogColor.Red);
                return ScheduleHandle.None;
            }

            if (count <= 0)
            {
                Log.Error($"[Scheduler] RegisterOverDuration: count 는 1 이상이어야 합니다. 무한 반복이 필요하면 RegisterInterval 을 쓰세요. (count: {count})", LogColor.Red);
                return ScheduleHandle.None;
            }

            if (fireImmediately && count < 2)
            {
                Log.Error("[Scheduler] RegisterOverDuration: fireImmediately 는 count 가 2 이상일 때만 씁니다. 1회 즉시 호출은 ScheduleOnce 를 쓰세요.", LogColor.Red);
                return ScheduleHandle.None;
            }

            if (onTick == null)
            {
                Log.Error("[Scheduler] RegisterOverDuration: onTick 이 null 입니다.", LogColor.Red);
                return ScheduleHandle.None;
            }

            //== fireImmediately 면 첫 호출이 0초에 일어나므로 남은 구간을 count-1 로 나눕니다.
            //== 어느 쪽이든 마지막 호출이 정확히 duration 시점에 오도록 맞춘 계산입니다.
            float interval;
            if (fireImmediately)
            {
                interval = duration / (count - 1);
            }
            else
            {
                interval = duration / count;
            }

            long id = Register(interval, count, onTick, onCompleted, isRealTime, fireImmediately, owner, trackOwner);
            return new ScheduleHandle(this, id);
        }

        private ScheduleHandle ScheduleOnceInternal(
            MonoBehaviour owner,
            bool trackOwner,
            float delay,
            Action onComplete,
            bool isRealTime)
        {
            if (delay < 0f)
            {
                Log.Error($"[Scheduler] ScheduleOnce: delay 는 음수일 수 없습니다. (delay: {delay})", LogColor.Red);
                return ScheduleHandle.None;
            }

            if (onComplete == null)
            {
                Log.Error("[Scheduler] ScheduleOnce: onComplete 가 null 입니다.", LogColor.Red);
                return ScheduleHandle.None;
            }

            //== onComplete 를 tick 이 아니라 완료 콜백 자리에 그대로 넘깁니다.
            //== tick 으로 감싸면 호출마다 람다가 새로 할당되는데, 1회 호출에는 그럴 이유가 없습니다.
            long id = Register(delay, 1, null, onComplete, isRealTime, false, owner, trackOwner);
            return new ScheduleHandle(this, id);
        }

        private long Register(
            float interval,
            int loopCount,
            Action<TickInfo> onTick,
            Action onCompleted,
            bool isRealTime,
            bool fireImmediately,
            MonoBehaviour owner,
            bool trackOwner)
        {
            long id = ++_lastScheduleId;

            Schedule schedule;
            if (_schedulePool.Count > 0)
            {
                schedule = _schedulePool.Dequeue();
            }
            else
            {
                schedule = new Schedule();
            }

            schedule.Reset(id, onTick, onCompleted, interval, loopCount, isRealTime, fireImmediately, owner, trackOwner);

            _schedules.Add(schedule);
            _scheduleById[id] = schedule;
            _activeScheduleCount = _schedules.Count;

            return id;
        }

        #endregion

        #region Private Helpers - Tick

        private void AdvanceTicks(Schedule schedule)
        {
            //== interval 이 0 이하면 매 프레임 1회 발화로 해석합니다.
            //== 그대로 아래 루프에 넣으면 경과 시간이 줄지 않아 빠져나오지 못하고 그 자리에서 멈춥니다.
            if (schedule.Interval <= 0f)
            {
                InvokeTick(schedule);
                TryComplete(schedule);
                return;
            }

            int firedCount = 0;
            while (schedule.ElapsedTime >= schedule.Interval)
            {
                schedule.ElapsedTime -= schedule.Interval;
                InvokeTick(schedule);

                if (TryComplete(schedule))
                {
                    return;
                }

                //== 콜백이 스스로를 취소하거나 정지시켰다면 이번 프레임의 나머지 발화를 중단합니다.
                if (schedule.IsCanceled || schedule.IsPaused)
                {
                    return;
                }

                firedCount++;
                if (firedCount >= MaxTicksPerFrame)
                {
                    //== 남은 누적 시간을 버려 다음 프레임에 다시 몰리지 않게 합니다.
                    schedule.ElapsedTime = 0f;
                    return;
                }
            }
        }

        //== 지정 횟수를 다 채웠으면 완료 콜백을 호출하고 취소 표시를 남깁니다.
        private bool TryComplete(Schedule schedule)
        {
            if (schedule.IsInfinite || schedule.IterationIndex < schedule.LoopCount)
            {
                return false;
            }

            InvokeCompleted(schedule);
            schedule.IsCanceled = true;

            return true;
        }

        private void InvokeTick(Schedule schedule)
        {
            int remaining;
            bool isLast;

            if (schedule.IsInfinite)
            {
                remaining = Schedule.InfiniteLoop;
                isLast = false;
            }
            else
            {
                remaining = schedule.LoopCount - schedule.IterationIndex - 1;
                isLast = remaining == 0;
            }

            TickInfo info = new TickInfo(schedule.IterationIndex, remaining, isLast, schedule.Id);

            //== 콜백보다 먼저 올려둡니다. 콜백이 예외를 던져도 횟수는 소모되어 같은 회차를 무한히 반복하지 않습니다.
            schedule.IterationIndex++;

            try
            {
                schedule.TickHandler?.Invoke(info);
            }
            catch (Exception e)
            {
                //== 한 스케줄의 예외가 다른 스케줄을 멈추지 않도록 여기서 삼킵니다.
                Log.Error($"[Scheduler] tick 콜백에서 예외 발생 (id: {schedule.Id}): {e}", LogColor.Red);
            }
        }

        private void InvokeCompleted(Schedule schedule)
        {
            try
            {
                schedule.CompletedHandler?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error($"[Scheduler] 완료 콜백에서 예외 발생 (id: {schedule.Id}): {e}", LogColor.Red);
            }
        }

        private void RemoveAt(int index)
        {
            Schedule schedule = _schedules[index];
            _schedules.RemoveAt(index);
            _scheduleById.Remove(schedule.Id);

            //== 풀에 넣기 전에 외부 참조를 끊습니다. 그대로 두면 재사용될 때까지 콜백과 owner 가 살아남습니다.
            schedule.TickHandler = null;
            schedule.CompletedHandler = null;
            schedule.Owner = null;

            _schedulePool.Enqueue(schedule);
        }

        #endregion
    }
}
