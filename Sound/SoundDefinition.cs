using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// 사운드 하나의 메타데이터(ID, 채널, 클립, 기본 볼륨)를 담는 에셋입니다.
    /// SoundManager.RegisterClip 에 넘겨 등록하거나, SoundLibrary 로 묶어 한 번에 등록합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSoundDefinition", menuName = "Cocoa/Lib/Sound/Sound Definition")]
    public sealed class SoundDefinition : ScriptableObject
    {
        #region Fields

        [SerializeField] private string _id;
        [SerializeField] private SoundChannel _channel = SoundChannel.SFX;
        [SerializeField] private AudioClip _clip;
        [SerializeField, Range(0f, 1f)] private float _defaultVolume = 1f;

        #endregion

        #region Properties

        /// <summary>SoundManager 에 등록할 때 키로 쓰는 식별자.</summary>
        public string Id
        {
            get { return _id; }
        }

        /// <summary>사운드가 속한 채널. Master 는 쓸 수 없습니다.</summary>
        public SoundChannel Channel
        {
            get { return _channel; }
        }

        /// <summary>오디오 클립. Addressables 로 따로 불러오는 구성이면 비어 있을 수 있습니다.</summary>
        public AudioClip Clip
        {
            get { return _clip; }
        }

        /// <summary>기본 재생 볼륨 (0 ~ 1). 채널 볼륨과 곱해져 최종 볼륨이 됩니다.</summary>
        public float DefaultVolume
        {
            get { return _defaultVolume; }
        }

        #endregion

        #region Unity Messages

#if UNITY_EDITOR
        private void OnValidate()
        {
            //== 등록 시점에 예외로 드러나기 전에 에셋을 고치는 시점에 알려줍니다.
            if (_channel == SoundChannel.Master)
            {
                Log.Error($"[SoundDefinition] '{name}' 의 채널이 Master 입니다. Master 는 클립을 가질 수 없으니 BGM, SFX, Voice 중에서 고르세요.", LogColor.Red);
            }
        }
#endif

        #endregion
    }
}
