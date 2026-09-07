using System;
using System.Reflection;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 전체 이름만으로 타입을 찾습니다.
    /// </summary>
    /// <remarks>
    /// 생성기는 방금 만든 .cs 를 컴파일 이후에야 참조할 수 있어 어느 어셈블리에 들어갈지 미리 알 수 없습니다.
    /// 그래서 로드된 어셈블리를 전부 훑습니다.
    /// </remarks>
    public static class TableReflection
    {
        #region Public API

        /// <summary>네임스페이스를 포함한 전체 이름으로 타입을 찾습니다. 없으면 null 을 반환합니다.</summary>
        public static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type;

                //== 동적 어셈블리나 부분 로드된 어셈블리는 GetType 자체가 던질 수 있어 개별로 격리합니다.
                try
                {
                    type = assemblies[i].GetType(fullName);
                }
                catch (Exception)
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        #endregion
    }
}
