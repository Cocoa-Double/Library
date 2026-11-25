namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// SoundManager 의 채널 상태를 보관하는 저장소 계약입니다.
    /// 기본 구현은 PlayerPrefsSoundSettings 이며, 서버 동기화나 세이브 파일에 합치려면 직접 구현해 SoundManager.Initialize 에 넘깁니다.
    /// </summary>
    /// <remarks>
    /// Save 는 볼륨 슬라이더를 끄는 동안 여러 번 불릴 수 있으므로 메모리에만 반영하고,
    /// 디스크나 네트워크에 쓰는 비용이 큰 작업은 Flush 에서 한 번에 처리하는 것을 전제로 합니다.
    /// </remarks>
    public interface ISoundSettingsStorage
    {
        /// <summary>채널 상태를 기록합니다. 영구 반영은 Flush 시점에 해도 됩니다.</summary>
        void Save(SoundChannel channel, SoundState state);

        /// <summary>저장된 채널 상태를 읽습니다. 저장된 값이 없으면 false 를 반환합니다.</summary>
        bool TryLoad(SoundChannel channel, out SoundState state);

        /// <summary>기록된 상태를 영구 저장소에 반영합니다. 앱이 백그라운드로 내려가거나 종료될 때 호출됩니다.</summary>
        void Flush();
    }
}
