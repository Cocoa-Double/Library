namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 직전 기록본을 따로 보관하는 저장소가 함께 구현하는 계약입니다.
    /// </summary>
    /// <remarks>
    /// 저장소는 파일이 읽히는지만 알 수 있고 내용이 온전한지는 모릅니다.
    /// 그래서 읽은 데이터를 해석하지 못했을 때 SaveManager 가 이 계약으로 백업을 한 번 더 시도합니다.
    /// </remarks>
    public interface ISaveBackupStorage
    {
        /// <summary>직전 기록본을 읽습니다. 백업이 없거나 읽지 못하면 false.</summary>
        bool TryReadBackup(string key, out string data);
    }
}
