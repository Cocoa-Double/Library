using PrimeTween;

namespace Cocoa.Lib.Tweening
{
    /// <summary>
    /// PrimeTween 트윈을 추적 없이 그대로 감싸는 경량 핸들입니다.
    /// <see cref="PrimeTweenHandler.Wrap"/> 이 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// PrimeTween 의 Tween 자체가 이미 풀을 가리키는 struct 핸들이라 딕셔너리 등록도 ID 발급도 하지 않습니다.
    /// 대신 개별 정지 여부를 기억하지 않으므로 전역 재개 때 개별 정지가 그대로 풀립니다.
    /// 한 번 재생하고 끝나는 연출에 쓰고, 보관했다가 제어할 트윈이면 <see cref="PrimeTweenHandler.Register"/> 를 쓰십시오.
    /// </remarks>
    public readonly struct PrimeTweenHandle
    {
        #region Static

        /// <summary>유효하지 않은 빈 핸들.</summary>
        public static PrimeTweenHandle None
        {
            get { return default; }
        }

        #endregion

        #region Fields

        private readonly Tween _tween;

        #endregion

        #region Properties

        /// <summary>핸들이 현재 살아 있는 트윈을 가리키는지 여부.</summary>
        public bool IsValid
        {
            get { return _tween.isAlive; }
        }

        #endregion

        #region Initialization

        internal PrimeTweenHandle(Tween tween)
        {
            _tween = tween;
        }

        #endregion

        #region Public API - Control

        /// <summary>트윈을 일시정지합니다.</summary>
        public void Pause()
        {
            //== readonly 필드라 프로퍼티를 직접 못 건드립니다. 복사본의 setter 도 ID 를 통해 실제 트윈에 닿습니다.
            Tween tween = _tween;
            if (tween.isAlive)
            {
                tween.isPaused = true;
            }
        }

        /// <summary>일시정지된 트윈을 재개합니다.</summary>
        public void Resume()
        {
            Tween tween = _tween;
            if (tween.isAlive)
            {
                tween.isPaused = false;
            }
        }

        /// <summary>트윈을 현재 값에서 즉시 중단합니다. 이후 이 핸들은 무효가 됩니다.</summary>
        public void Stop()
        {
            Tween tween = _tween;
            if (tween.isAlive)
            {
                tween.Stop();
            }
        }

        /// <summary>트윈을 끝값으로 즉시 완료시킵니다. 이후 이 핸들은 무효가 됩니다.</summary>
        public void Complete()
        {
            Tween tween = _tween;
            if (tween.isAlive)
            {
                tween.Complete();
            }
        }

        #endregion
    }
}
