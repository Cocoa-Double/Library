using UnityEngine;

using Conditional = System.Diagnostics.ConditionalAttribute;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 로그 출력 색상 (에디터 콘솔 리치 텍스트용).
    /// </summary>
    public enum LogColor
    {
        /// <summary>색상을 적용하지 않습니다.</summary>
        None = 0,
        Red,
        Yellow,
        Green,
        Blue,
        Magenta
    }

    /// <summary>
    /// 로그 레벨.
    /// </summary>
    public enum LogLevel
    {
        Info = 0,
        Warning,
        Error,
        Assert
    }

    /// <summary>
    /// 로그 출력을 담당하는 정적 클래스.
    /// 출력 차단은 두 단계로 이루어집니다.
    /// 컴파일 단계에서 Info/Warning/Assert 호출은 릴리스 빌드에 아예 포함되지 않으며(Conditional),
    /// 런타임 단계에서 <see cref="Enabled"/> 로 남은 출력을 끌 수 있습니다.
    /// </summary>
    public static class Log
    {
        #region Static Fields

        //== LogColor 를 리치 텍스트 태그 이름으로 변환하는 표. 인덱스는 (int)LogColor 와 1:1로 대응합니다.
        //== enum 의 ToString 은 호출마다 문자열을 할당하므로 로그 경로에서는 사용하지 않습니다.
        private static readonly string[] _colorNames =
        {
            null,
            "red",
            "yellow",
            "green",
            "blue",
            "magenta"
        };

        //== 리치 텍스트는 에디터 콘솔에서만 해석됩니다.
        //== 빌드에서 태그를 붙이면 Player.log 에 태그가 문자 그대로 남으므로 색을 적용하지 않습니다.
        private static readonly bool _colorSupported = Application.isEditor;

        #endregion

        #region Properties

        /// <summary>
        /// 로그 출력 여부. false 이면 Info/Warning/Assert 가 출력되지 않습니다.
        /// Error 는 이 스위치의 영향을 받지 않습니다. 릴리스 빌드에서 장애 원인을 추적할 수 있는
        /// 유일한 경로이기 때문입니다.
        /// 기본값은 에디터와 개발 빌드에서 true, 릴리스 빌드에서 false 입니다.
        /// </summary>
        public static bool Enabled { get; set; } = Debug.isDebugBuild;

        #endregion

        #region Public API - Info

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Info(object message, LogColor color = LogColor.None)
        {
            LogMessage(message, LogLevel.Info, color);
        }

        #endregion

        #region Public API - Warning

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(object message, LogColor color = LogColor.None)
        {
            LogMessage(message, LogLevel.Warning, color);
        }

        #endregion

        #region Public API - Error

        public static void Error(object message, LogColor color = LogColor.None)
        {
            LogMessage(message, LogLevel.Error, color);
        }

        #endregion

        #region Public API - Assert

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, object message, LogColor color = LogColor.Red)
        {
            if (condition)
            {
                return;
            }

            LogMessage(message, LogLevel.Assert, color);
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// 로그를 출력하는 공통 메서드.
        /// 릴리스에서는 Info/Warning/Assert 호출이 Conditional 로 제거되므로
        /// 이 메서드는 사실상 Error 경로로만 진입합니다.
        /// </summary>
        private static void LogMessage(object message, LogLevel level, LogColor color)
        {
            //== Error 는 전역 스위치와 무관하게 항상 출력합니다.
            if (!Enabled && level != LogLevel.Error)
            {
                return;
            }

            string output = ApplyColor(message != null ? message.ToString() : "null", color);
            switch (level)
            {
                case LogLevel.Info:
                    {
                        Debug.Log(output);
                        break;
                    }
                case LogLevel.Warning:
                    {
                        Debug.LogWarning(output);
                        break;
                    }
                case LogLevel.Error:
                    {
                        Debug.LogError(output);
                        break;
                    }
                case LogLevel.Assert:
                    {
                        Debug.LogAssertion(output);
                        break;
                    }
            }
        }

        private static string ApplyColor(string message, LogColor color)
        {
            if (color == LogColor.None || !_colorSupported)
            {
                return message;
            }

            int index = (int)color;
            if (index < 0 || index >= _colorNames.Length)
            {
                return message;
            }

            return string.Concat("<color=", _colorNames[index], ">", message, "</color>");
        }

        #endregion
    }
}
