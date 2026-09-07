using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 구글 시트 기반 테이블 생성기의 설정입니다.
    /// 시트 접근 정보, 파싱 규칙, 필드 클러스터, enum 외부 참조, 출력 경로, CSV 백업을 한 에셋에 모아 둡니다.
    /// </summary>
    /// <remarks>
    /// 프로젝트마다 달라지는 값은 전부 여기로 빼고 생성기에는 상수를 남기지 않습니다.
    /// ApiKey 는 이 에셋에 그대로 저장되므로 저장소에 함께 올라갑니다. 공개 저장소에서는 비워 두고 각자 채우세요.
    /// </remarks>
    [CreateAssetMenu(fileName = "TableGenSettings", menuName = "Cocoa/Lib/Table Gen Settings")]
    public sealed class TableGenSettings : ScriptableObject
    {
        #region Nested Types

        /// <summary>생성 대상 시트 탭 하나입니다.</summary>
        [Serializable]
        public sealed class SheetTarget
        {
            [Tooltip("생성될 클래스명 = 시트 탭 이름")]
            public string SheetName;

            [Tooltip("탭의 gid. 비워 두면 SheetName 으로 조회합니다")]
            public string Gid;

            [Tooltip("이 탭을 생성에 포함할지 여부")]
            public bool Enabled = true;
        }

        /// <summary>
        /// 접두사가 같은 여러 컬럼을 서브 클래스 하나로 묶는 매핑입니다.
        /// FieldPrefix 를 "Reward" 로 두면 헤더의 n_RewardID / n_RewardCount / e_RewardType 을 Reward 인스턴스로 채웁니다.
        /// </summary>
        [Serializable]
        public sealed class FieldCluster
        {
            [Tooltip("접두사를 제거한 헤더 필드명의 시작 부분. 'Reward' 로 두면 RewardID / RewardCount 를 묶습니다")]
            public string FieldPrefix;

            [Tooltip("묶어서 담을 서브 클래스의 전체 이름. 비우면 TableNamespace 아래에 새로 만듭니다")]
            public string SubClassTypeName;

            [Tooltip("이 매핑 사용 여부")]
            public bool Enabled = true;
        }

        /// <summary>게임 코드에 이미 있는 enum 을 그대로 참조하게 하는 매핑입니다. 걸리면 enum .cs 를 생성하지 않습니다.</summary>
        [Serializable]
        public sealed class EnumExternalMap
        {
            [Tooltip("enum 의 단순 이름. 헤더의 '#별칭' 또는 자동 생성명(시트명+필드명)과 일치해야 합니다")]
            public string TypeName;

            [Tooltip("참조할 enum 의 전체 이름")]
            public string FullTypeName;

            [Tooltip("이 매핑 사용 여부")]
            public bool Enabled = true;
        }

        /// <summary>특정 시트의 Table 클래스가 상속할 베이스 클래스 매핑입니다. 베이스에 이미 있는 필드는 다시 선언하지 않습니다.</summary>
        [Serializable]
        public sealed class SheetBaseClass
        {
            [Tooltip("시트 탭 이름. 정확히 일치해야 합니다")]
            public string SheetName;

            [Tooltip("상속할 베이스 클래스의 전체 이름")]
            public string BaseClassFullName;

            [Tooltip("이 매핑 사용 여부")]
            public bool Enabled = true;
        }

        #endregion

        #region Fields - 시트 접근

        [Header("구글 시트 접근")]
        [Tooltip("스프레드시트 ID (URL 의 /d/ 와 /edit 사이)")]
        public string SpreadsheetId;

        [Tooltip("Google Sheets API 키. 이 에셋에 저장되므로 저장소에 올라갑니다")]
        public string ApiKey;

        [Tooltip("생성할 시트 탭 목록")]
        public List<SheetTarget> Sheets = new List<SheetTarget>();

        [Tooltip("이 접두사로 시작하는 시트는 목록과 생성에서 제외합니다. 기본값 '[' 는 대괄호로 묶은 분류용 시트를 걸러 냅니다")]
        public List<string> IgnoredSheetPrefixes = new List<string> { "[" };

        #endregion

        #region Fields - 파싱 규칙

        [Header("파싱 규칙")]
        [Tooltip("List 타입(ns_ 등) 값의 원소 구분자")]
        public string ListSeparator = "^";

        [Tooltip("헤더가 있는 행 (1부터)")]
        public int HeaderRow = 1;

        [Tooltip("설명 행 (1부터). 읽지 않고 건너뜁니다")]
        public int DescriptionRow = 2;

        [Tooltip("값이 시작되는 행 (1부터)")]
        public int DataStartRow = 3;

        #endregion

        #region Fields - 시트 상속

        [Header("시트 상속")]
        [Tooltip("특정 시트의 Table 클래스가 상속할 베이스 클래스")]
        public List<SheetBaseClass> SheetBaseClasses = new List<SheetBaseClass>();

        #endregion

        #region Fields - 필드 클러스터

        [Header("필드 클러스터")]
        [Tooltip("같은 접두사를 가진 여러 컬럼을 서브 클래스 하나로 묶습니다")]
        public List<FieldCluster> FieldClusters = new List<FieldCluster>();

        #endregion

        #region Fields - enum 외부 참조

        [Header("Enum 외부 참조")]
        [Tooltip("특정 이름의 enum 을 게임 코드의 것으로 재사용합니다. 걸리면 enum .cs 를 생성하지 않습니다")]
        public List<EnumExternalMap> EnumExternalMaps = new List<EnumExternalMap>();

        #endregion

        #region Fields - 자동 생성 제외

        [Header("자동 클래스 생성 제외")]
        [Tooltip("이 목록의 시트는 데이터만 읽고 Table 클래스 .cs 를 생성하지 않습니다. MasterTable / enum / 서브 클래스는 그대로 생성합니다")]
        public List<string> DontAutoClass = new List<string>();

        #endregion

        #region Fields - 출력

        [Header("출력 경로 / 네임스페이스")]
        [Tooltip("Table 클래스 .cs 출력 폴더. 클러스터 서브 클래스는 이 아래 Clusters 폴더에 생성됩니다")]
        public string TableOutputPath = "Assets/Scripts/Table";

        [Tooltip("MasterTable 클래스 .cs 출력 폴더")]
        public string MasterTableOutputPath = "Assets/Scripts/Table/MasterTable";

        [Tooltip("MasterTable .asset 출력 폴더. Addressable 관리 편의를 위해 .cs 와 분리합니다. 비우면 MasterTableOutputPath 와 같습니다")]
        public string MasterTableAssetPath = "Assets/Data/MasterTables";

        [Tooltip("생성될 Table 클래스의 네임스페이스. 생성 결과는 게임 데이터이므로 라이브러리와 분리합니다")]
        public string TableNamespace = "Game.Table";

        [Tooltip("생성될 MasterTable 클래스의 네임스페이스")]
        public string MasterTableNamespace = "Game.MasterTable";

        [Tooltip("ITableData 와 MasterTableSO 가 들어 있는 네임스페이스. 생성된 코드의 using 에 들어갑니다")]
        public string RuntimeNamespace = "Cocoa.Lib.MasterTable";

        #endregion

        #region Fields - CSV 백업

        [Header("CSV 백업")]
        [Tooltip("CSV 백업 사용 여부")]
        public bool EnableCsvBackup = true;

        [Tooltip("원본 시트 데이터를 CSV 로 저장할 폴더")]
        public string CsvBackupPath = TableCsvBackup.DefaultOutputFolder;

        #endregion

        #region Unity Messages

        private void OnValidate()
        {
            //== 행 번호가 0 이나 음수면 인덱스가 음수가 되어 헤더를 못 찾거나 엉뚱한 행을 데이터로 읽습니다.
            if (HeaderRow < 1)
            {
                HeaderRow = 1;
            }

            if (DescriptionRow < 1)
            {
                DescriptionRow = 1;
            }

            if (DataStartRow <= HeaderRow)
            {
                DataStartRow = HeaderRow + 1;
            }
        }

        #endregion
    }
}
