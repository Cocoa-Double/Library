using System;
using System.Globalization;
using System.IO;
using System.Text;

using Newtonsoft.Json;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 버퍼를 재사용해 GC 할당을 줄인 Newtonsoft 직렬화기입니다. 서버 전송처럼 직렬화가 잦은 경로에서 NewtonsoftSerializer 대신 씁니다.
    /// </summary>
    /// <remarks>
    /// JsonSerializer, StringBuilder, MemoryStream 을 인스턴스가 들고 재사용합니다.
    /// 버퍼를 공유하므로 한 인스턴스를 여러 스레드에서 동시에 쓰면 안 됩니다. Default 는 스레드마다 따로 만들어집니다.
    /// 경로별 할당은 다음과 같습니다.
    /// Serialize 는 중간 버퍼를 재사용하지만 반환하는 string 은 매번 새로 만듭니다.
    /// SerializeTo 는 호출자의 Stream 에 바로 쓰므로 string 과 byte[] 를 만들지 않습니다.
    /// SerializeToUtf8 은 내부 버퍼에 쓰고 복사 없이 그 구간을 돌려주며, 다음 호출 전까지만 유효합니다.
    /// </remarks>
    public sealed class PooledJsonSerializer : ISaveSerializer
    {
        #region Constants

        private const int InitialBufferCapacity = 2048;
        private const int StreamWriterBufferSize = 1024;

        //== 한 번 큰 데이터를 직렬화하면 버퍼가 그 크기로 남으므로, 이보다 커진 버퍼는 다음 호출에서 새로 만듭니다.
        private const int MaxRetainedCapacity = 256 * 1024;

        #endregion

        #region Static Fields

        [ThreadStatic] private static PooledJsonSerializer _default;

        //== 앞에 BOM 이 붙으면 JSON 으로 읽지 못하는 서버가 있어 BOM 없는 인코딩을 씁니다.
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        #endregion

        #region Static Properties

        /// <summary>스레드별 공용 인스턴스. 스레드마다 자기 버퍼를 가지므로 여러 스레드에서 써도 안전합니다.</summary>
        public static PooledJsonSerializer Default
        {
            get { return _default ?? (_default = new PooledJsonSerializer()); }
        }

        #endregion

        #region Fields

        private readonly JsonSerializer _serializer;
        private StringBuilder _stringBuilder;
        private MemoryStream _byteStream;

        #endregion

        #region Constructors

        /// <param name="settings">
        /// 직렬화 설정. null 이면 NewtonsoftSerializer.CreateDefaultSettings 를 써서 NewtonsoftSerializer 와 같은 결과를 냅니다.
        /// ContractResolver 가 비어 있으면 UnitySerializationContractResolver 를 채워 넣습니다.
        /// </param>
        public PooledJsonSerializer(JsonSerializerSettings settings = null)
        {
            if (settings == null)
            {
                settings = NewtonsoftSerializer.CreateDefaultSettings();
            }
            else if (settings.ContractResolver == null)
            {
                settings.ContractResolver = UnitySerializationContractResolver.Instance;
            }

            _serializer = JsonSerializer.Create(settings);
            _stringBuilder = new StringBuilder(InitialBufferCapacity);
            _byteStream = new MemoryStream(InitialBufferCapacity);
        }

        #endregion

        #region Public API

        public string Serialize<T>(T value)
        {
            if (_stringBuilder.Capacity > MaxRetainedCapacity)
            {
                _stringBuilder = new StringBuilder(InitialBufferCapacity);
            }

            _stringBuilder.Clear();
            using (StringWriter stringWriter = new StringWriter(_stringBuilder, CultureInfo.InvariantCulture))
            using (JsonTextWriter jsonWriter = new JsonTextWriter(stringWriter))
            {
                _serializer.Serialize(jsonWriter, value, typeof(T));
            }

            return _stringBuilder.ToString();
        }

        public T Deserialize<T>(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return default;
            }

            using (StringReader stringReader = new StringReader(raw))
            using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
            {
                return _serializer.Deserialize<T>(jsonReader);
            }
        }

        /// <summary>
        /// 호출자가 준 스트림에 UTF-8 JSON 을 바로 씁니다. string 과 byte[] 를 만들지 않습니다.
        /// 스트림은 닫지 않으며 수명은 호출자가 관리합니다.
        /// </summary>
        public void SerializeTo<T>(T value, Stream destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            //== 작성기는 작은 객체라 호출마다 만들고, 비용이 큰 버퍼와 계약 캐시만 재사용합니다.
            StreamWriter streamWriter = new StreamWriter(destination, _utf8NoBom, StreamWriterBufferSize, true);
            JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter);
            _serializer.Serialize(jsonWriter, value, typeof(T));
            jsonWriter.Flush();
            streamWriter.Flush();
        }

        /// <summary>
        /// 내부 버퍼에 UTF-8 JSON 으로 쓰고, 복사 없이 그 구간을 돌려줍니다.
        /// 예: new ByteArrayContent(segment.Array, segment.Offset, segment.Count)
        /// </summary>
        /// <remarks>
        /// 돌려받은 구간은 이 인스턴스의 다음 직렬화 호출 전까지만 유효합니다. 다음 호출이 같은 버퍼를 덮어씁니다.
        /// ByteArrayContent 는 배열을 복사하지 않고 참조만 하므로, 전송이 끝나기 전에 같은 인스턴스로 다시 직렬화하면 보내는 데이터가 깨집니다.
        /// 병렬이나 지연 전송이 필요하면 구간을 복사하거나 SerializeTo 로 별도 스트림에 씁니다.
        /// </remarks>
        public ArraySegment<byte> SerializeToUtf8<T>(T value)
        {
            if (_byteStream.Capacity > MaxRetainedCapacity)
            {
                _byteStream = new MemoryStream(InitialBufferCapacity);
            }

            _byteStream.Position = 0;
            _byteStream.SetLength(0);
            SerializeTo(value, _byteStream);

            //== GetBuffer 는 내부 배열을 복사 없이 내주므로 유효 길이는 Length 로 잘라 씁니다.
            return new ArraySegment<byte>(_byteStream.GetBuffer(), 0, (int)_byteStream.Length);
        }

        #endregion
    }
}
