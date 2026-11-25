using System;
using UnityEngine;

namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// 채널 하나의 볼륨과 음소거 상태입니다.
    /// 값을 바꿀 때는 With 계열 메서드로 새 값을 만들어 SoundManager.SetState 에 넘깁니다.
    /// </summary>
    public readonly struct SoundState : IEquatable<SoundState>
    {
        #region Fields

        private readonly bool _isMuted;
        private readonly float _volume;

        #endregion

        #region Properties

        /// <summary>음소거 여부.</summary>
        public bool IsMuted
        {
            get { return _isMuted; }
        }

        /// <summary>채널 볼륨 (0 ~ 1). 음소거와 별개로 유지되어 음소거를 풀면 이 값으로 돌아갑니다.</summary>
        public float Volume
        {
            get { return _volume; }
        }

        /// <summary>음소거가 아니고 볼륨이 1 인 기본 상태.</summary>
        public static SoundState Default
        {
            get { return new SoundState(false, 1f); }
        }

        #endregion

        #region Constructors

        public SoundState(bool isMuted, float volume)
        {
            _isMuted = isMuted;

            //== 저장소에서 읽은 값이 범위를 벗어나 있어도 그대로 퍼지지 않도록 생성 시점에 막습니다.
            _volume = Mathf.Clamp01(volume);
        }

        #endregion

        #region Public API

        /// <summary>볼륨만 바꾼 새 상태를 반환합니다.</summary>
        public SoundState WithVolume(float volume)
        {
            return new SoundState(_isMuted, volume);
        }

        /// <summary>음소거 여부만 바꾼 새 상태를 반환합니다.</summary>
        public SoundState WithMute(bool isMuted)
        {
            return new SoundState(isMuted, _volume);
        }

        public bool Equals(SoundState other)
        {
            return _isMuted == other._isMuted && Mathf.Approximately(_volume, other._volume);
        }

        public override bool Equals(object obj)
        {
            return obj is SoundState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (_isMuted ? 1 : 0) ^ _volume.GetHashCode();
        }

        public static bool operator ==(SoundState left, SoundState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SoundState left, SoundState right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"SoundState(muted={_isMuted}, volume={_volume:F2})";
        }

        #endregion
    }
}
