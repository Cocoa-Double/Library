using System.Collections.Generic;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 직렬화된 문자열을 키 단위로 읽고 쓰는 저장 매체 계약입니다.
    /// 키는 inventory, settings, slot_1 처럼 논리적인 저장 단위입니다.
    /// </summary>
    /// <remarks>
    /// 구현은 예외를 던지지 않고 성공 여부로 결과를 알리며, 실패 원인은 내부에서 로그로 남깁니다.
    /// 세이브 한 번의 실패가 게임 흐름 전체를 끊지 않게 하기 위해서입니다.
    /// </remarks>
    public interface ISaveStorage
    {
        /// <summary>해당 키의 데이터가 있는지 확인합니다.</summary>
        bool Exists(string key);

        /// <summary>데이터를 기록합니다. 성공하면 true.</summary>
        bool Write(string key, string data);

        /// <summary>데이터를 읽습니다. 없거나 읽지 못하면 false.</summary>
        bool TryRead(string key, out string data);

        /// <summary>키를 삭제합니다. 지운 것이 있으면 true.</summary>
        bool Delete(string key);

        /// <summary>저장된 모든 키를 반환합니다.</summary>
        IReadOnlyList<string> GetAllKeys();

        /// <summary>모든 데이터를 삭제합니다.</summary>
        void DeleteAll();
    }
}
