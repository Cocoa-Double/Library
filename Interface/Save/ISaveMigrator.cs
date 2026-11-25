namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 한 버전의 세이브 데이터를 바로 다음 버전 형식으로 바꾸는 마이그레이터입니다.
    /// </summary>
    /// <remarks>
    /// SaveManager 는 저장된 버전부터 현재 버전까지 등록된 마이그레이터를 차례로 적용합니다(v1 → v2 → v3).
    /// 입력과 출력은 데이터를 직렬화한 문자열이며, 바꾸지 않는 필드도 결과에 그대로 담아야 보존됩니다.
    /// </remarks>
    public interface ISaveMigrator
    {
        /// <summary>입력으로 받는 버전. 출력은 FromVersion + 1 형식이어야 합니다.</summary>
        int FromVersion { get; }

        /// <summary>FromVersion 형식의 데이터를 FromVersion + 1 형식으로 바꿔 반환합니다.</summary>
        string Migrate(string payload);
    }
}
