using System;
using UnityEditor;
using UnityEngine.Networking;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 에디터에서 외부 의존 없이 GET 요청을 보냅니다.
    /// </summary>
    /// <remarks>
    /// 에디터에는 PlayerLoop 이 돌지 않아 코루틴도 UniTask 도 쓸 수 없어 EditorApplication.update 로 완료를 폴링합니다.
    /// </remarks>
    public static class TableWebRequest
    {
        #region Nested Types

        public sealed class Response
        {
            public bool Success;
            public long Code;
            public string Error;
            public string Text;
        }

        #endregion

        #region Constants

        //== 응답이 오지 않는 요청이 창을 계속 작업 중 상태로 붙잡지 않게 합니다.
        private const int TimeoutSeconds = 30;

        #endregion

        #region Public API

        /// <summary>url 로 GET 합니다. 성공이든 실패든 완료 시 onComplete 를 한 번 호출합니다.</summary>
        public static void Get(string url, Action<Response> onComplete)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!operation.isDone)
                {
                    return;
                }

                //== 콜백에서 예외가 나도 폴링이 남지 않도록 먼저 떼어 냅니다.
                EditorApplication.update -= poll;

                Response response = new Response
                {
                    Success = request.result == UnityWebRequest.Result.Success,
                    Code = request.responseCode,
                    Error = request.error,
                    Text = request.downloadHandler != null ? request.downloadHandler.text : null
                };
                request.Dispose();

                if (onComplete != null)
                {
                    onComplete(response);
                }
            };

            EditorApplication.update += poll;
        }

        #endregion
    }
}
