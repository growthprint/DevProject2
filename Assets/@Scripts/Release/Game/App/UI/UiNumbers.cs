#nullable enable annotations
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 화면의 숫자 입력 칸이 함께 쓰는 읽기 규칙이다.
    internal static class UiNumbers
    {
        // 옵션 감도와 난이도 사용자 지정 입력이 함께 쓰는 숫자 파싱이다. 로캘과 무관하게 읽고 NaN과 무한대는 거부한다.
        internal static bool TryParseFinite(string? text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }

    // 음이 아닌 정수를 재사용 글자 배열에 10진수로 쓰는 공용 도우미다. 결과는 InvariantCulture의 "0…" 형식과 같다.
    // 철수 카운트다운, 결과 화면 경험치, HUD 시계가 문자열 할당 없이 TMP SetCharArray로 넘길 때 쓴다.
    internal static class UiDigits
    {
        // value를 minimumDigits자리 이상 0으로 채워 index부터 쓰고 다음 위치를 돌려준다. 자릿수가 넘치면 늘려 쓴다.
        // value가 0 이상인지는 호출자가 보장한다.
        internal static int Write(char[] buffer, int index, long value, int minimumDigits)
        {
            int digits = 1;
            for (long rest = value; rest >= 10; rest /= 10) digits++;
            if (digits < minimumDigits) digits = minimumDigits;
            int end = index + digits;
            for (int position = end - 1; position >= index; position--)
            {
                buffer[position] = (char)('0' + value % 10);
                value /= 10;
            }
            return end;
        }
    }

    // 매 프레임 string.Format이 만드는 박싱과 새 문자열을 피하려고, 정수 세 개를 넣는 형식 문자열을 재사용 글자 배열에 직접 쓴다.
    // 분해 결과는 분해한 형식 문자열 참조에 묶어 두고, 제작 도구나 인스펙터가 형식을 바꿔 참조가 달라지면 다시 분해한다.
    internal sealed class UiDigitFormat
    {
        // 조각의 argument가 -1이면 형식 문자열의 [start, start+length) 리터럴이고, 아니면 argument번 인수를 length자리 이상 0으로 채워 쓴다.
        // 분해할 수 없는 형식이면 _parts가 null이다.
        string? _parsedFormat;
        (int argument, int start, int length)[]? _parts;
        char[] _chars = new char[0];

        // 형식 조각대로 재사용 배열에 쓰고 쓴 길이만 SetCharArray로 넘긴다. TMP는 이 경로도 text처럼 리치 텍스트를 처리한다.
        // 분해할 수 없는 형식이거나 인수가 음수면 false를 돌려 호출자가 string.Format을 쓰게 한다.
        internal bool TryWrite(TMP_Text target, string format, int a0, int a1, int a2)
        {
            if (!ReferenceEquals(_parsedFormat, format))
            {
                _parsedFormat = format;
                _parts = Parse(format, out int capacity);
                if (_chars.Length < capacity) _chars = new char[capacity];
            }
            (int argument, int start, int length)[]? parts = _parts;
            if (parts == null) return false;
            if (a0 < 0 || a1 < 0 || a2 < 0) return false;
            int written = 0;
            for (int index = 0; index < parts.Length; index++)
            {
                (int argument, int start, int length) part = parts[index];
                if (part.argument < 0)
                {
                    format.CopyTo(part.start, _chars, written, part.length);
                    written += part.length;
                }
                else written = UiDigits.Write(_chars, written,
                    part.argument == 0 ? a0 : part.argument == 1 ? a1 : a2, part.length);
            }
            target.SetCharArray(_chars, 0, written);
            return true;
        }

        // 리터럴과 0 채움 정수 자리인 {n} 또는 {n:0…}만 분해한다. n은 0에서 2까지이고 결과는 InvariantCulture 형식과 같다.
        // 중괄호 이스케이프, 정렬, 다른 형식 지정이 있으면 null을 돌려 Format을 쓰게 한다.
        // capacity는 음이 아닌 인수로 쓸 수 있는 최대 글자 수다. int는 최대 10자리다.
        static (int argument, int start, int length)[]? Parse(string? format, out int capacity)
        {
            capacity = 0;
            if (format == null) return null;
            var parts = new List<(int argument, int start, int length)>();
            int literalStart = 0, index = 0;
            while (index < format.Length)
            {
                char current = format[index];
                if (current == '}') return null;
                if (current != '{') { index++; continue; }
                if (index > literalStart) parts.Add((-1, literalStart, index - literalStart));
                index++;
                if (index >= format.Length || format[index] < '0' || format[index] > '2') return null;
                int argument = format[index++] - '0';
                int width = 1;
                if (index < format.Length && format[index] == ':')
                {
                    width = 0;
                    while (++index < format.Length && format[index] == '0') width++;
                    if (width == 0) return null;
                }
                if (index >= format.Length || format[index] != '}') return null;
                index++;
                parts.Add((argument, 0, width));
                literalStart = index;
            }
            if (format.Length > literalStart) parts.Add((-1, literalStart, format.Length - literalStart));
            foreach ((int argument, int start, int length) part in parts)
                capacity += part.argument < 0 ? part.length : Mathf.Max(part.length, 10);
            return parts.ToArray();
        }
    }
}
