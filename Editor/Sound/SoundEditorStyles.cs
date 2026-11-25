using UnityEngine;

using Cocoa.Lib.Sound;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// 사운드 에디터 화면이 함께 쓰는 색상 정의입니다. 인스펙터와 라이브러리 창의 채널 색이 어긋나지 않게 한곳에 둡니다.
    /// </summary>
    public static class SoundEditorStyles
    {
        #region Static Fields

        private static readonly Color _bgmColor = new Color(0.24f, 0.72f, 0.72f);
        private static readonly Color _sfxColor = new Color(0.91f, 0.60f, 0.25f);
        private static readonly Color _voiceColor = new Color(0.61f, 0.48f, 0.82f);
        private static readonly Color _masterColor = new Color(0.4f, 0.4f, 0.4f);

        #endregion

        #region Public API

        /// <summary>채널을 나타내는 색을 반환합니다.</summary>
        public static Color GetChannelColor(SoundChannel channel)
        {
            switch (channel)
            {
                case SoundChannel.BGM:
                    return _bgmColor;
                case SoundChannel.SFX:
                    return _sfxColor;
                case SoundChannel.Voice:
                    return _voiceColor;
                default:
                    return _masterColor;
            }
        }

        #endregion
    }
}
