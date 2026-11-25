namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// 사운드 채널. 채널마다 볼륨과 음소거 상태를 따로 가집니다.
    /// </summary>
    public enum SoundChannel
    {
        //== 배경음. 동시에 하나만 재생됩니다.
        BGM = 0,

        //== 효과음. 여러 개가 겹쳐 재생됩니다.
        SFX = 1,

        //== 음성. 효과음과 볼륨을 따로 조절하기 위해 분리합니다.
        Voice = 2,

        //== 전체 볼륨. 클립을 등록할 수 없고 AudioListener 로 모든 소리에 적용됩니다.
        Master = 3
    }
}
