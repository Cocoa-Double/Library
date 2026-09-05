using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 임의의 <see cref="IEnumerator"/> 코루틴을 실행하고 관리하는 엔진입니다.
    /// MonoBehaviour 가 아닌 객체에서도 코루틴을 돌릴 수 있게 합니다.
    /// </summary>
    /// <remarks>
    /// 일시정지를 StopCoroutine 이 아니라 게이트 방식으로 처리합니다.
    /// 코루틴을 죽였다 다시 시작하면 진행 위치와 대기 중이던 yield 명령이 사라지므로,
    /// yield 경계에서 대기시키는 쪽을 택했습니다. 대신 정지된 코루틴도 매 프레임 깨어나 조건을 확인합니다.
    /// </remarks>
    public class CoroutineHandler : MonoBehaviour
    {
        #region Nested Types

        //== 실행 중인 코루틴 한 건. Drive 가 참조로 상태를 읽어야 하므로 클래스로 둡니다.
        private sealed class Entry
        {
            public long Id;
            public IEnumerator Routine;
            public Coroutine Driver;

            //== 개별 일시정지 플래그. PauseAll 과는 독립적으로 유지됩니다.
            public bool IsPaused;

            //== true 면 PauseAll 의 영향을 받지 않습니다. 개별 Pause 는 그대로 적용됩니다.
            public bool IgnorePause;

            //== RunFor 로 등록된 경우에만 true. Owner 가 파괴되면 코루틴도 정리합니다.
            public bool TrackOwner;
            public MonoBehaviour Owner;
        }

        #endregion

        #region Static

        private static CoroutineHandler _defaultInstance;

        /// <summary>
        /// 라이브러리가 제공하는 공용 인스턴스. 첫 접근 시 DontDestroyOnLoad GameObject 를 만듭니다.
        /// </summary>
        public static CoroutineHandler Default
        {
            get
            {
                if (_defaultInstance != null)
                {
                    return _defaultInstance;
                }

                GameObject host = new GameObject("[Cocoa.Lib] CoroutineHandler");
                DontDestroyOnLoad(host);
                _defaultInstance = host.AddComponent<CoroutineHandler>();

                return _defaultInstance;
            }
        }

        #endregion

        #region Fields

        //== 코루틴 ID -> 실행 항목
        private readonly Dictionary<long, Entry> _entriesById = new Dictionary<long, Entry>();

        //== 마지막으로 발급한 코루틴 ID. 0 은 빈 핸들용으로 예약되어 있어 첫 발급은 1 입니다.
        private long _lastCoroutineId;

        //== 전역 일시정지 상태. 게이트에서 매번 확인하므로 이 값을 켜두면 새로 등록되는 코루틴도 정지 상태로 시작합니다.
        [SerializeField] private bool _allPaused;

        #endregion

        #region Properties

        /// <summary>추적 중인 코루틴 수. 일시정지된 것도 포함합니다.</summary>
        public int ActiveCount
        {
            get { return _entriesById.Count; }
        }

        /// <summary>전역 일시정지 상태인지 여부.</summary>
        public bool IsPausedAll
        {
            get { return _allPaused; }
        }

        #endregion

        #region Public API - Run

        /// <summary>
        /// 코루틴을 실행하고 핸들을 반환합니다. 코루틴이 끝나면 추적에서 자동으로 빠집니다.
        /// </summary>
        /// <param name="routine">실행할 IEnumerator.</param>
        /// <param name="ignorePause">true 면 PauseAll 의 영향을 받지 않습니다.</param>
        /// <returns>실행에 실패하면 <see cref="CoroutineHandle.None"/> 을 반환합니다.</returns>
        public CoroutineHandle Run(IEnumerator routine, bool ignorePause = false)
        {
            return RunInternal(routine, null, false, ignorePause);
        }

        /// <summary>
        /// owner 의 수명에 묶어 코루틴을 실행합니다. owner 가 파괴되면 다음 yield 경계에서 중지됩니다.
        /// </summary>
        /// <param name="owner">코루틴 수명을 맞출 대상.</param>
        /// <param name="routine">실행할 IEnumerator.</param>
        /// <param name="ignorePause">true 면 PauseAll 의 영향을 받지 않습니다.</param>
        public CoroutineHandle RunFor(MonoBehaviour owner, IEnumerator routine, bool ignorePause = false)
        {
            if (owner == null)
            {
                Log.Error("[CoroutineHandler] RunFor: owner 가 null 입니다.", LogColor.Red);
                return CoroutineHandle.None;
            }

            return RunInternal(routine, owner, true, ignorePause);
        }

        #endregion

        #region Public API - Pause

        /// <summary>지정한 코루틴을 일시정지합니다. 다음 yield 경계에서 적용됩니다.</summary>
        public void Pause(long id)
        {
            if (_entriesById.TryGetValue(id, out Entry entry))
            {
                entry.IsPaused = true;
            }
        }

        /// <summary>
        /// 전역 일시정지 상태로 진입합니다. ignorePause 로 등록된 코루틴은 면제됩니다.
        /// 개별 플래그는 건드리지 않습니다. 게이트가 이 값을 직접 확인하므로 이후 등록되는 코루틴도 정지 상태로 시작합니다.
        /// </summary>
        public void PauseAll()
        {
            _allPaused = true;
        }

        #endregion

        #region Public API - Resume

        /// <summary>지정한 코루틴의 개별 일시정지를 해제합니다. 전역 일시정지 중이면 그대로 멈춰 있습니다.</summary>
        public void Resume(long id)
        {
            if (_entriesById.TryGetValue(id, out Entry entry))
            {
                entry.IsPaused = false;
            }
        }

        /// <summary>
        /// 전역 일시정지 상태를 해제합니다.
        /// 개별 Pause 로 멈춰둔 코루틴은 계속 멈춰 있습니다. 전역 정지가 개별 의도를 덮어쓰지 않게 하기 위함입니다.
        /// </summary>
        public void ResumeAll()
        {
            _allPaused = false;
        }

        #endregion

        #region Public API - Stop

        /// <summary>지정한 코루틴을 중지하고 추적에서 제거합니다.</summary>
        public void Stop(long id)
        {
            if (!_entriesById.TryGetValue(id, out Entry entry))
            {
                return;
            }

            if (entry.Driver != null)
            {
                StopCoroutine(entry.Driver);
            }

            _entriesById.Remove(id);
        }

        /// <summary>추적 중인 모든 코루틴을 중지합니다.</summary>
        public void StopAll()
        {
            //== StopCoroutine 은 Drive 를 즉시 끊어 Remove 가 실행되지 않으므로, 순회 중 컬렉션이 변하지 않습니다.
            foreach (Entry entry in _entriesById.Values)
            {
                if (entry.Driver != null)
                {
                    StopCoroutine(entry.Driver);
                }
            }

            _entriesById.Clear();
        }

        #endregion

        #region Public API - Queries

        /// <summary>해당 ID 의 코루틴을 추적 중인지 여부. 일시정지 상태도 추적 중으로 봅니다.</summary>
        public bool IsActive(long id)
        {
            return _entriesById.ContainsKey(id);
        }

        #endregion

        #region Unity Messages

        private void OnDestroy()
        {
            //== 공용 인스턴스가 파괴되면 정적 참조를 끊어 다음 접근에서 새로 만들게 합니다.
            if (_defaultInstance == this)
            {
                _defaultInstance = null;
            }
        }

        #endregion

        #region Private Helpers

        private CoroutineHandle RunInternal(IEnumerator routine, MonoBehaviour owner, bool trackOwner, bool ignorePause)
        {
            if (routine == null)
            {
                Log.Warning("[CoroutineHandler] null 루틴은 실행할 수 없습니다.", LogColor.Yellow);
                return CoroutineHandle.None;
            }

            //== 비활성 상태에서 StartCoroutine 을 호출하면 예외가 납니다. 미리 걸러 원인을 남깁니다.
            if (!isActiveAndEnabled)
            {
                Log.Error("[CoroutineHandler] 비활성 상태에서는 코루틴을 실행할 수 없습니다.", LogColor.Red);
                return CoroutineHandle.None;
            }

            long id = ++_lastCoroutineId;
            Entry entry = new Entry
            {
                Id = id,
                Routine = routine,
                Owner = owner,
                TrackOwner = trackOwner,
                IgnorePause = ignorePause
            };

            _entriesById.Add(id, entry);
            entry.Driver = StartCoroutine(Drive(entry));

            return new CoroutineHandle(this, id);
        }

        /// <summary>
        /// 사용자 코루틴을 한 단계씩 진행시키는 드라이버.
        /// 매 yield 경계에서 owner 생존 여부와 일시정지 게이트를 확인하고, 예외를 격리합니다.
        /// </summary>
        private IEnumerator Drive(Entry entry)
        {
            IEnumerator inner = entry.Routine;

            while (true)
            {
                //== owner 가 사라졌으면 코루틴도 함께 정리합니다.
                if (entry.TrackOwner && entry.Owner == null)
                {
                    break;
                }

                //== 일시정지 게이트. 정지 중에도 위로 돌아가 owner 생존 여부를 계속 확인합니다.
                if (entry.IsPaused || (_allPaused && !entry.IgnorePause))
                {
                    yield return null;
                    continue;
                }

                bool moved;
                try
                {
                    moved = inner.MoveNext();
                }
                catch (Exception e)
                {
                    //== 한 코루틴의 예외가 다른 코루틴을 멈추지 않도록 여기서 삼키고 해당 코루틴만 종료합니다.
                    Log.Error($"[CoroutineHandler] 코루틴 실행 중 예외 (id: {entry.Id}): {e}", LogColor.Red);
                    moved = false;
                }

                if (!moved)
                {
                    break;
                }

                //== 사용자 코루틴이 내놓은 yield 명령을 그대로 전달합니다.
                yield return inner.Current;
            }

            _entriesById.Remove(entry.Id);
        }

        #endregion
    }
}
