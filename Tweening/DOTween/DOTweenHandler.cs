using System.Collections.Generic;
using UnityEngine;

using DG.Tweening;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Tweening
{
    /// <summary>
    /// DOTween 트윈의 생명주기를 추적하고 제어하는 백엔드입니다.
    /// 인스턴스 기반이며 전역으로 쓸 수 있는 <see cref="Default"/> 를 함께 제공합니다.
    /// </summary>
    /// <remarks>
    /// 등록하면 백엔드 중립인 <see cref="TweenHandle"/> 을 돌려주므로, 받는 쪽은 DOTween 을 쓰는지 몰라도 됩니다.
    /// <para>
    /// 이 어셈블리는 COCOALIB_DOTWEEN 심볼이 정의됐을 때만 컴파일됩니다. UPM 패키지로 넣었다면 versionDefines 가 자동으로 켜 주고,
    /// 에셋스토어 .unitypackage 로 넣었다면 Utility Panel 에서 ASMDEF 를 만든 뒤 심볼을 직접 추가해야 합니다.
    /// </para>
    /// </remarks>
    public class DOTweenHandler : ITweenController, ITweenBackend
    {
        #region Static

        /// <summary>라이브러리가 제공하는 공용 인스턴스입니다.</summary>
        public static DOTweenHandler Default { get; } = new DOTweenHandler();

        #endregion

        #region Fields

        private readonly Dictionary<long, Tween> _tweens = new Dictionary<long, Tween>();

        //== 개별로 멈춰 둔 트윈의 ID. ResumeAll 이 이것들까지 되살리지 않게 하려고 따로 기억합니다.
        private readonly HashSet<long> _individuallyPaused = new HashSet<long>();

        //== KillAll 순회 중 onKill 콜백이 _tweens 를 건드리므로, 스냅샷을 떠서 돌기 위한 재사용 버퍼입니다.
        private readonly List<Tween> _iterateBuffer = new List<Tween>();

        //== 0 은 무효 ID 로 예약. 첫 발급은 1 부터입니다.
        private long _idCounter;

        //== 전역 일시정지 상태. 이 상태에서 새로 등록되는 트윈도 멈춘 채로 시작합니다.
        private bool _allPaused;

        //== onKill 안에서 KillAll 이 다시 불리면 _iterateBuffer 가 순회 도중 비워집니다.
        private bool _isKillingAll;

        #endregion

        #region Properties

        /// <summary>전역 일시정지 상태인지 여부.</summary>
        public bool IsPausedAll
        {
            get { return _allPaused; }
        }

        /// <summary>현재 추적 중인 트윈 수.</summary>
        public int TrackedCount
        {
            get { return _tweens.Count; }
        }

        #endregion

        #region Initialization

        //== 도메인 리로드를 끈 프로젝트에서는 이전 실행의 딕셔너리가 그대로 남습니다.
        //== 그 안의 Tween 은 DOTween 이 이미 정리한 껍데기라, 다음 실행의 PauseAll 이 죽은 트윈을 건드립니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            Default._tweens.Clear();
            Default._individuallyPaused.Clear();
            Default._iterateBuffer.Clear();
            Default._idCounter = 0L;
            Default._allPaused = false;
            Default._isKillingAll = false;
        }

        #endregion

        #region Public API - Register

        /// <summary>트윈을 등록하고 핸들을 돌려줍니다.</summary>
        /// <param name="tween">등록할 Tween. Sequence 도 됩니다.</param>
        /// <param name="ignoreTimeScale">true 면 Time.timeScale 의 영향을 받지 않습니다.</param>
        /// <remarks>
        /// 완료든 중단이든 외부 강제 종료든 트윈이 Kill 되면 onKill 을 통해 스스로 빠집니다.
        /// null 이나 비활성 트윈은 등록하지 않고 <see cref="TweenHandle.None"/> 을 돌려줍니다.
        /// 전역 일시정지 중에 등록하면 그 트윈도 멈춘 채로 시작합니다.
        /// </remarks>
        public TweenHandle Register(Tween tween, bool ignoreTimeScale = false)
        {
            if (tween == null || !tween.active)
            {
                Log.Warning("[DOTweenHandler] null 이거나 비활성인 트윈은 등록할 수 없습니다.", LogColor.Yellow);
                return TweenHandle.None;
            }

            if (ignoreTimeScale)
            {
                tween.SetUpdate(true);
            }

            long id = ++_idCounter;
            _tweens.Add(id, tween);

            //== onComplete 가 아니라 onKill 에 겁니다. 중단이나 외부 Kill 에서도 불려야 누수가 없습니다.
            //== = 이 아니라 += 로 걸어 사용자가 미리 지정한 onKill 을 덮어쓰지 않습니다.
            //== 캡처 클래스와 델리게이트를 합쳐 등록 한 번당 약 88 바이트가 듭니다. 이 라이브러리에서 유일한 등록 비용입니다.
            tween.onKill += () =>
            {
                Unregister(id);
            };

            //== 전역 일시정지 중이면 신규 트윈도 멈춘 채로 시작합니다.
            if (_allPaused)
            {
                tween.Pause();
            }

            return new TweenHandle(this, id);
        }

        /// <summary>
        /// 트윈을 등록하면서 owner 의 GameObject 에 수명을 묶습니다. owner 가 파괴되면 트윈도 함께 정리됩니다.
        /// </summary>
        public TweenHandle RegisterFor(MonoBehaviour owner, Tween tween, bool ignoreTimeScale = false)
        {
            if (owner == null)
            {
                Log.Warning("[DOTweenHandler] owner 가 null 이라 수명을 묶을 수 없습니다.", LogColor.Yellow);
                return TweenHandle.None;
            }

            TweenHandle handle = Register(tween, ignoreTimeScale);
            if (handle.IsValid)
            {
                tween.SetLink(owner.gameObject, LinkBehaviour.KillOnDestroy);
            }

            return handle;
        }

        #endregion

        #region Public API - Pause

        /// <summary>해당 트윈을 일시정지합니다.</summary>
        public void Pause(long id)
        {
            if (!_tweens.TryGetValue(id, out Tween tween))
            {
                return;
            }

            //== 전역 재개 때 되살리지 않도록 개별 정지였다는 사실을 남깁니다.
            _individuallyPaused.Add(id);
            tween.Pause();
        }

        /// <summary>모든 트윈을 일시정지합니다. 이후 새로 등록되는 트윈도 멈춘 채로 시작합니다.</summary>
        public void PauseAll()
        {
            _allPaused = true;

            //== Pause 는 트윈을 Kill 하지 않으므로 _tweens 가 바뀌지 않습니다. 직접 순회해도 안전합니다.
            foreach (Tween tween in _tweens.Values)
            {
                tween.Pause();
            }
        }

        #endregion

        #region Public API - Resume

        /// <summary>
        /// 해당 트윈의 개별 정지를 풉니다. 전역 일시정지 중이라면 표시만 지우고 재생은 <see cref="ResumeAll"/> 때 일어납니다.
        /// </summary>
        public void Resume(long id)
        {
            if (!_tweens.TryGetValue(id, out Tween tween))
            {
                return;
            }

            _individuallyPaused.Remove(id);

            //== 전역이 멈춰 있는데 하나만 되살리면 PauseAll 의 의미가 깨집니다.
            if (_allPaused)
            {
                return;
            }

            tween.Play();
        }

        /// <summary>전역 일시정지를 풀고 트윈을 재개합니다. 개별로 멈춰 둔 트윈은 멈춘 채로 둡니다.</summary>
        public void ResumeAll()
        {
            _allPaused = false;

            foreach (KeyValuePair<long, Tween> pair in _tweens)
            {
                if (_individuallyPaused.Contains(pair.Key))
                {
                    continue;
                }

                pair.Value.Play();
            }
        }

        #endregion

        #region Public API - Kill

        /// <summary>해당 트윈을 종료합니다. complete 가 true 면 끝값으로 완료시킨 뒤 종료합니다.</summary>
        public void Kill(long id, bool complete = false)
        {
            if (!_tweens.TryGetValue(id, out Tween tween))
            {
                return;
            }

            //== onKill 이 Unregister 를 부르므로 여기서 직접 빼지 않습니다.
            tween.Kill(complete);
        }

        /// <summary>추적 중인 모든 트윈을 종료합니다.</summary>
        public void KillAll(bool complete = false)
        {
            //== onKill 안에서 KillAll 이 다시 불리면 바깥 순회가 쓰던 버퍼가 비워집니다.
            //== 이미 전부 종료하는 중이므로 안쪽 호출은 그냥 무시합니다.
            if (_isKillingAll || _tweens.Count == 0)
            {
                return;
            }

            _isKillingAll = true;
            try
            {
                //== Kill 이 onKill 을 타고 Unregister 를 부르면서 순회 중 _tweens 가 바뀝니다. 스냅샷을 뜹니다.
                _iterateBuffer.Clear();
                _iterateBuffer.AddRange(_tweens.Values);

                for (int i = 0; i < _iterateBuffer.Count; i++)
                {
                    _iterateBuffer[i].Kill(complete);
                }
            }
            finally
            {
                _iterateBuffer.Clear();
                _isKillingAll = false;
            }
        }

        #endregion

        #region Public API - Rewind / Restart

        /// <summary>해당 트윈을 시작 지점으로 되감고 멈춥니다.</summary>
        public void Rewind(long id)
        {
            if (!_tweens.TryGetValue(id, out Tween tween))
            {
                return;
            }

            //== DOTween 의 Rewind 는 되감으면서 정지 상태로 둡니다. 개별 정지와 같은 상태이므로 표시를 맞춥니다.
            _individuallyPaused.Add(id);
            tween.Rewind();
        }

        /// <summary>추적 중인 모든 트윈을 시작 지점으로 되감고 멈춥니다.</summary>
        public void RewindAll()
        {
            //== Rewind 는 트윈을 Kill 하지 않으므로 직접 순회해도 안전합니다.
            foreach (KeyValuePair<long, Tween> pair in _tweens)
            {
                _individuallyPaused.Add(pair.Key);
                pair.Value.Rewind();
            }
        }

        /// <summary>해당 트윈을 시작 지점으로 되감고 다시 재생합니다.</summary>
        public void Restart(long id)
        {
            if (!_tweens.TryGetValue(id, out Tween tween))
            {
                return;
            }

            _individuallyPaused.Remove(id);
            tween.Restart();

            //== 전역이 멈춰 있는데 Restart 가 재생까지 해버리면 PauseAll 의 의미가 깨집니다.
            if (_allPaused)
            {
                tween.Pause();
            }
        }

        /// <summary>추적 중인 모든 트윈을 시작 지점으로 되감고 다시 재생합니다.</summary>
        public void RestartAll()
        {
            _individuallyPaused.Clear();

            foreach (Tween tween in _tweens.Values)
            {
                tween.Restart();

                if (_allPaused)
                {
                    tween.Pause();
                }
            }
        }

        #endregion

        #region Public API - Query

        /// <summary>해당 ID 의 트윈이 아직 살아 있는지 여부.</summary>
        /// <remarks>
        /// 딕셔너리에 있는지만 보지 않고 active 까지 확인합니다.
        /// onKill 을 거치지 않고 무효화되는 경로가 생겨도 죽은 핸들을 유효하다고 답하지 않기 위해서입니다.
        /// </remarks>
        public bool IsAlive(long id)
        {
            return _tweens.TryGetValue(id, out Tween tween) && tween.active;
        }

        #endregion

        #region Private Helpers

        private void Unregister(long id)
        {
            _tweens.Remove(id);
            _individuallyPaused.Remove(id);
        }

        #endregion
    }
}
