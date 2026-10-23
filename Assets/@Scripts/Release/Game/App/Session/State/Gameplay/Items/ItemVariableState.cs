#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 아이템 변수를 자료형 번호와 16진수 바이트로 보관한다. 수량이나 내구도처럼 전용 필드가 있는 값은 담지 않는다.
    public sealed class ItemVariableState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public ItemVariableState(string key, int dataType, string dataHex, bool display)
        {
            Key = key; DataType = dataType; DataHex = dataHex; Display = display;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string key, int dataType, string dataHex)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (IsDedicatedFieldKey(key)) return false;
            if (dataType < _firstDataType || dataType > _lastDataType) return false;
            if (dataHex == null || dataHex.Length % 2 != 0)
                return false;
            foreach (char digit in dataHex)
                if (!Uri.IsHexDigit(digit)) return false;
            return HasValidPayloadLength(dataType, dataHex);
        }

        // 전용 필드에 저장하거나 탄약에서 계산하는 키다. 변수로도 받으면 같은 값이 두 번 저장되므로 거절한다.
        static bool IsDedicatedFieldKey(string key) =>
            key == "Count" || key == "Durability" || key == "DurabilityLoss" || key == "Inspected" ||
            key == "ItemFromInfo" || key == "BulletCount";

        // 값이 있으면 실수와 정수 자료형은 4바이트, 불리언은 1바이트여야 한다. 빈 값은 어느 자료형이든 받는다.
        static bool HasValidPayloadLength(int dataType, string dataHex) =>
            !((dataType == _floatDataType || dataType == _intDataType) && dataHex.Length != 0 && dataHex.Length != _fourByteHexLength ||
                dataType == _boolDataType && dataHex.Length != 0 && dataHex.Length != _oneByteHexLength);

        // 원본 변수 자료형 번호의 범위와 값이 정해진 자료형이다. 실수와 정수 번호는 제작 도구가 원본 상수를 읽는 규칙과 같다.
        // 저장 파일에 들어가는 값이라 바꾸면 안 된다.
        const int _firstDataType = 0;
        const int _floatDataType = 1;
        const int _intDataType = 2;
        const int _lastDataType = 4;
        // 4바이트와 1바이트 값의 16진 문자 수다.
        const int _fourByteHexLength = 8;
        const int _oneByteHexLength = 2;

        // 원본 1바이트 불리언 변수의 자료형 번호다. 저장 파일에 들어가는 값이라 바꾸면 안 된다.
        internal const int _boolDataType = 3;

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string key, int dataType, string dataHex, bool display,
            [NotNullWhen(true)] out ItemVariableState? value)
        {
            value = Check(key, dataType, dataHex) ? new ItemVariableState(key, dataType, dataHex, display) : null;
            return value != null;
        }

        public string Key { get; }
        public int DataType { get; }
        public string DataHex { get; }
        public bool Display { get; }
    }
}
