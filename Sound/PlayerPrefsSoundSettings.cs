using UnityEngine;

namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// PlayerPrefs 에 채널 상태를 보관하는 ISoundSettingsStorage 기본 구현입니다.
    /// 저장소를 따로 넘기지 않으면 SoundManager 가 이 구현을 씁니다.
    /// </summary>
    public sealed class PlayerPrefsSoundSettings : ISoundSettingsStorage
    {
        #region Constants

        //== 다른 PlayerPrefs 키와 겹치지 않도록 라이브러리 접두사를 붙입니다.
        private const string KeyPrefix = "Cocoa.Lib.Sound.";
        private const string MuteSuffix = ".Mute";
        private const string VolumeSuffix = ".Volume";

        #endregion

        #region Public API

        public void Save(SoundChannel channel, SoundState state)
        {
            //== PlayerPrefs.Set 은 메모리에만 반영되므로 슬라이더 조작 중 반복 호출되어도 부담이 없습니다.
            PlayerPrefs.SetInt(GetMuteKey(channel), state.IsMuted ? 1 : 0);
            PlayerPrefs.SetFloat(GetVolumeKey(channel), state.Volume);
        }

        public bool TryLoad(SoundChannel channel, out SoundState state)
        {
            string muteKey = GetMuteKey(channel);
            string volumeKey = GetVolumeKey(channel);

            //== 둘 중 하나라도 없으면 한쪽만 기본값으로 섞이지 않도록 저장된 적 없는 것으로 봅니다.
            if (!PlayerPrefs.HasKey(muteKey) || !PlayerPrefs.HasKey(volumeKey))
            {
                state = SoundState.Default;
                return false;
            }

            state = new SoundState(PlayerPrefs.GetInt(muteKey) != 0, PlayerPrefs.GetFloat(volumeKey));
            return true;
        }

        public void Flush()
        {
            PlayerPrefs.Save();
        }

        #endregion

        #region Private Helpers

        private static string GetMuteKey(SoundChannel channel)
        {
            return KeyPrefix + channel + MuteSuffix;
        }

        private static string GetVolumeKey(SoundChannel channel)
        {
            return KeyPrefix + channel + VolumeSuffix;
        }

        #endregion
    }
}
