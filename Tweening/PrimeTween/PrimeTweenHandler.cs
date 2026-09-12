using System.Collections.Generic;
using UnityEngine;

using PrimeTween;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Tweening
{
    /// <summary>
    /// PrimeTween 트윈을 제어하는 백엔드입니다.
    /// 인스턴스 기반이며 전역으로 쓸 수 있는 <see cref="Default"/> 를 함께 제공합니다.
    /// </summary>
    /// <remarks>
    /// 등록 경로가 둘입니다. <see cref="Register"/> 는 추적하고 백엔드 중립인 <see cref="TweenHandle"/> 을 돌려주며,
    /// <see cref="Wrap"/> 은 추적 없이 <see cref="PrimeTweenHandle"/> 로 감싸기만 해 무할당 이점을 남깁니다.
    /// <para>
    /// <see cref="PauseAll"/> 과 <see cref="KillAll"/> 은 PrimeTween 의 전역 API 에 위임하므로
    /// 이 백엔드로 등록하지 않은 트윈까지 포함해 모든 PrimeTween 트윈에 영향을 줍니다.
    /// </para>
    /// <para>
    /// useUnscaledTime 은 트윈을 만들 때 정하는 값이라 등록 단계에서 바꾸지 않습니다.
    /// 필요하면 생성 시 TweenSettings.useUnscaledTime 으로 지정하십시오.
    /// </para>
    /// </remarks>
    public class PrimeTweenHandler : ITweenController, ITweenBackend
    {
        #region Constants

        //== 죽은 항목을 훑기 시작하는 최소 크기. 이보다 작으면 훑을 가치가 없습니다.
        private const int MinimumSweepThreshold = 32;

        #endregion

        #region Static

        /// <summary>라이브러리가 제공하는 공용 인스턴스입니다.</summary>
        public static PrimeTweenHandler Default { get; } = new PrimeTweenHandler();

        #endregion

        #region Fields

        private readonly Dictionary<long, Tween> _tweens = new Dictionary<long, Tween>();

        //== 개별로 멈춰 둔 트윈의 ID. ResumeAll 이 이것들까지 되살리지 않게 하려고 따로 기억합니다.
        private readonly HashSet<long> _individuallyPaused = new HashSet<long>();

        //== 순회 중 딕셔너리를 지우지 않으려고 지울 ID 를 모아 두는 재사용 버퍼입니다.
        private readonly List<long> _iterateBuffer = new List<long>();

        //== 0 은 무효 ID 로 예약. 첫 발급은 1 부터입니다.
        private long _idCounter;

        //== 전역 일시정지 상태. 이 상태에서 새로 등록되는 트윈도 멈춘 채로 시작합니다.
        private bool _allPaused;

        //== 추적 목록이 이 크기에 닿으면 죽은 항목을 한 번 훑습니다.
        private int _sweepThreshold = MinimumSweepThreshold;

        #endregion

        #region Properties

        /// <summary>전역 일시정지 상태인지 여부.</summary>
        public bool IsPausedAll
        {
            get { return _allPaused; }
        }

        /// <summary>현재 추적 목록에 들어 있는 항목 수. 아직 정리되지 않은 죽은 항목도 포함합니다.</summary>
        public int TrackedCount
        {
            get { return _tweens.Count; }
        }

        #endregion

        #region Initialization

        //== 도메인 리로드를 끈 프로젝트에서는 이전 실행의 추적 목록이 그대로 남습니다.
        //== 그 안의 Tween 은 다음 실행에서 다른 트윈에 재사용됐을 수 있는 풀 인덱스입니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            Default._tweens.Clear();
            Default._individuallyPaused.Clear();
            Default._iterateBuffer.Clear();
            Default._idCounter = 0L;
            Default._allPaused = false;
            Default._sweepThreshold = MinimumSweepThreshold;
        }

        #endregion

        #region Public API - Register

        /// <summary>트윈을 추적 목록에 넣고 백엔드 중립 핸들을 돌려줍니다.</summary>
        /// <remarks>
        /// 죽은 트윈은 등록하지 않고 <see cref="TweenHandle.None"/> 을 돌려줍니다.
        /// 전역 일시정지 중에 등록하면 그 트윈도 멈춘 채로 시작합니다.
        /// 대상이 파괴되거나 트윈이 끝나면 PrimeTween 이 알아서 정리하므로 owner 를 따로 묶을 필요가 없습니다.
        /// </remarks>
        public TweenHandle Register(Tween tween)
        {
            if (!tween.isAlive)
            {
                Log.Warning("[PrimeTweenHandler] 죽은 트윈은 등록할 수 없습니다.", LogColor.Yellow);
                return TweenHandle.None;
            }

            //== 죽은 항목이 스스로 빠지지 않으므로 등록 시점에 한 번씩 걷어냅니다.
            if (_tweens.Count >= _sweepThreshold)
            {
                SweepDead();
            }

            if (_allPaused)
            {
                tween.isPaused = true;
            }

            long id = ++_idCounter;
            _tweens.Add(id, tween);

            return new TweenHandle(this, id);
        }

        /// <summary>트윈을 추적 없이 감싸기만 해서 돌려줍니다. 등록 비용이 들지 않습니다.</summary>
        /// <remarks>전역 일시정지 중이면 시작 상태만 맞춰 주고, 이후 개별 정지 여부는 기억하지 않습니다.</remarks>
        public PrimeTweenHandle Wrap(Tween tween)
        {
            if (!tween.isAlive)
            {
                Log.Warning("[PrimeTweenHandler] 죽은 트윈은 감쌀 수 없습니다.", LogColor.Yellow);
                return PrimeTweenHandle.None;
            }

            if (_allPaused)
            {
                tween.isPaused = true;
            }

            return new PrimeTweenHandle(tween);
        }

        #endregion

        #region Public API - Pause

        /// <summary>해당 트윈을 일시정지합니다.</summary>
        public void Pause(long id)
        {
            if (!TryGetAlive(id, out Tween tween))
            {
                return;
            }

            //== 전역 재개 때 되살리지 않도록 개별 정지였다는 사실을 남깁니다.
            _individuallyPaused.Add(id);
            tween.isPaused = true;
        }

        /// <summary>모든 트윈을 일시정지합니다. 이후 등록되는 트윈도 멈춘 채로 시작합니다.</summary>
        public void PauseAll()
        {
            _allPaused = true;
            Tween.SetPausedAll(true);
        }

        /// <summary>특정 대상의 모든 트윈을 일시정지하거나 재개합니다. 추적 여부와 무관하게 걸립니다.</summary>
        public void SetPaused(object target, bool isPaused)
        {
            Tween.SetPausedAll(isPaused, target);
        }

        #endregion

        #region Public API - Resume

        /// <summary>
        /// 해당 트윈의 개별 정지를 풉니다. 전역 일시정지 중이라면 표시만 지우고 재생은 <see cref="ResumeAll"/> 때 일어납니다.
        /// </summary>
        public void Resume(long id)
        {
            if (!TryGetAlive(id, out Tween tween))
            {
                return;
            }

            _individuallyPaused.Remove(id);

            //== 전역이 멈춰 있는데 하나만 되살리면 PauseAll 의 의미가 깨집니다.
            if (_allPaused)
            {
                return;
            }

            tween.isPaused = false;
        }

        /// <summary>전역 일시정지를 풀고 트윈을 재개합니다. 개별로 멈춰 둔 트윈은 다시 멈춥니다.</summary>
        /// <remarks>
        /// PrimeTween 의 전역 재개는 개별 정지 여부를 구분하지 않고 전부 되살리므로, 한 번 다 풀고 나서 되돌립니다.
        /// 추적하지 않은 트윈의 개별 정지는 알 방법이 없어 그대로 풀립니다.
        /// </remarks>
        public void ResumeAll()
        {
            _allPaused = false;
            Tween.SetPausedAll(false);

            if (_individuallyPaused.Count == 0)
            {
                return;
            }

            foreach (long id in _individuallyPaused)
            {
                if (_tweens.TryGetValue(id, out Tween tween) && tween.isAlive)
                {
                    tween.isPaused = true;
                }
            }
        }

        #endregion

        #region Public API - Kill

        /// <summary>해당 트윈을 종료합니다. complete 가 true 면 끝값으로 완료시킨 뒤 종료합니다.</summary>
        public void Kill(long id, bool complete = false)
        {
            if (!TryGetAlive(id, out Tween tween))
            {
                return;
            }

            if (complete)
            {
                tween.Complete();
            }
            else
            {
                tween.Stop();
            }

            //== Stop 과 Complete 는 콜백을 주지 않으므로 여기서 직접 뺍니다.
            Unregister(id);
        }

        /// <summary>모든 트윈을 종료합니다. complete 가 true 면 끝값으로 완료시킨 뒤 종료합니다.</summary>
        public void KillAll(bool complete = false)
        {
            if (complete)
            {
                Tween.CompleteAll();
            }
            else
            {
                Tween.StopAll();
            }

            //== 전역 종료라 살아남는 항목이 없습니다. 훑지 않고 통째로 비웁니다.
            _tweens.Clear();
            _individuallyPaused.Clear();
            _sweepThreshold = MinimumSweepThreshold;
        }

        /// <summary>특정 대상의 모든 트윈을 중단하거나 완료합니다. 추적 목록에서는 다음 훑기 때 정리됩니다.</summary>
        public void Stop(object target, bool complete = false)
        {
            if (complete)
            {
                Tween.CompleteAll(target);
            }
            else
            {
                Tween.StopAll(target);
            }
        }

        #endregion

        #region Public API - Rewind / Restart

        /// <summary>해당 트윈을 시작 지점으로 되감고 멈춥니다.</summary>
        /// <remarks>
        /// PrimeTween 에는 Rewind 가 없어 elapsedTimeTotal 을 0 으로 되돌리는 것으로 대신합니다.
        /// 되감기 전에 지나간 콜백은 다시 불리지 않습니다.
        /// </remarks>
        public void Rewind(long id)
        {
            if (!TryGetAlive(id, out Tween tween))
            {
                return;
            }

            _individuallyPaused.Add(id);
            tween.elapsedTimeTotal = 0f;
            tween.isPaused = true;
        }

        /// <summary>추적 중인 모든 트윈을 시작 지점으로 되감고 멈춥니다.</summary>
        public void RewindAll()
        {
            foreach (KeyValuePair<long, Tween> pair in _tweens)
            {
                Tween tween = pair.Value;
                if (!tween.isAlive)
                {
                    continue;
                }

                _individuallyPaused.Add(pair.Key);
                tween.elapsedTimeTotal = 0f;
                tween.isPaused = true;
            }
        }

        /// <summary>해당 트윈을 시작 지점으로 되감고 다시 재생합니다.</summary>
        /// <remarks>PrimeTween 에는 Restart 가 없어 되감기와 재생을 이어 붙인 것입니다.</remarks>
        public void Restart(long id)
        {
            if (!TryGetAlive(id, out Tween tween))
            {
                return;
            }

            _individuallyPaused.Remove(id);
            tween.elapsedTimeTotal = 0f;

            //== 전역이 멈춰 있는데 Restart 가 재생까지 해버리면 PauseAll 의 의미가 깨집니다.
            tween.isPaused = _allPaused;
        }

        /// <summary>추적 중인 모든 트윈을 시작 지점으로 되감고 다시 재생합니다.</summary>
        public void RestartAll()
        {
            _individuallyPaused.Clear();

            foreach (Tween pooled in _tweens.Values)
            {
                Tween tween = pooled;
                if (!tween.isAlive)
                {
                    continue;
                }

                tween.elapsedTimeTotal = 0f;
                tween.isPaused = _allPaused;
            }
        }

        #endregion

        #region Public API - Query

        /// <summary>해당 ID 의 트윈이 아직 살아 있는지 여부.</summary>
        /// <remarks>죽은 항목을 발견하면 그 자리에서 추적 목록에서 뺍니다.</remarks>
        public bool IsAlive(long id)
        {
            return TryGetAlive(id, out _);
        }

        #endregion

        #region Private Helpers

        //== 살아 있는 트윈만 꺼냅니다. 죽어 있으면 발견한 김에 추적 목록에서 뺍니다.
        private bool TryGetAlive(long id, out Tween tween)
        {
            if (!_tweens.TryGetValue(id, out tween))
            {
                return false;
            }

            if (tween.isAlive)
            {
                return true;
            }

            Unregister(id);
            return false;
        }

        private void Unregister(long id)
        {
            _tweens.Remove(id);
            _individuallyPaused.Remove(id);
        }

        //== PrimeTween 은 트윈이 끝날 때 콜백을 주지 않아 DOTween 처럼 onKill 로 스스로 빠지게 할 수 없습니다.
        //== 그래서 등록할 때마다 목록이 임계값에 닿았는지만 보고 한 번씩 훑고, 남은 수의 두 배를 다음 임계값으로 잡습니다.
        //== 살아 있는 트윈이 늘어난 만큼 훑는 간격도 벌어지므로 등록 한 번당 상환 비용이 상수로 유지됩니다.
        private void SweepDead()
        {
            _iterateBuffer.Clear();

            foreach (KeyValuePair<long, Tween> pair in _tweens)
            {
                if (!pair.Value.isAlive)
                {
                    _iterateBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _iterateBuffer.Count; i++)
            {
                Unregister(_iterateBuffer[i]);
            }

            _iterateBuffer.Clear();
            _sweepThreshold = Mathf.Max(MinimumSweepThreshold, _tweens.Count * 2);
        }

        #endregion
    }
}
