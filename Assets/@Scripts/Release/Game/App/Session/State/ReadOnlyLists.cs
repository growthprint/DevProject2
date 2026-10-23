#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 불변 상태 값이 받은 목록을 방어적으로 복사하고 항목 키의 중복을 판정한다. 던지지 않고 bool로만 답하며,
    // 실패 보고와 값의 범위 규칙은 각 상태 형식이 맡는다.
    internal static class ReadOnlyLists
    {
        // 신뢰 생성자가 목록을 검증 없이 복사할 때 쓴다. QuestProgress의 받은 보상 ID처럼 생략할 수 있는 목록 인수의 null은 빈 목록으로 받는다.
        internal static IReadOnlyList<T> CopyOrEmpty<T>(IReadOnlyList<T>? source)
        {
            if (source == null || source.Count == 0) return Array.Empty<T>();
            var copy = new T[source.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = source[index];
            return Array.AsReadOnly(copy);
        }

        // null은 '아직 포착하지 않음'을 뜻하므로 그대로 두고, 목록만 검증 없이 복사한다.
        internal static IReadOnlyList<T>? CopyOrNull<T>(IReadOnlyList<T>? source) => source == null ? null : CopyOrEmpty(source);

        // 저장 값 목록은 불변이라 끝에 하나를 붙인 새 배열을 만든다. 소유자들이 드문 명령에서만 부르고 목록이 짧아 LINQ 없이 복사한다.
        internal static T[] Appended<T>(IReadOnlyList<T> source, T value)
        {
            var appended = new T[source.Count + 1];
            for (int index = 0; index < source.Count; index++) appended[index] = source[index];
            appended[source.Count] = value;
            return appended;
        }

        // 복사 없이 판정만 한다. 목록이 null이면 거부하고, 항목마다 null, 키 중복, valid 실패 순서로 본다.
        // 정수 ID 같은 값 형식 항목은 키 선택자로 항등 함수를 넘긴다.
        internal static bool AreUnique<T, TKey>([NotNullWhen(true)] IReadOnlyList<T>? source, Func<T, TKey> key,
            Func<T, bool>? valid = null)
        {
            if (source == null) return false;
            var ids = new HashSet<TKey>();
            for (int index = 0; index < source.Count; index++)
            {
                T item = source[index];
                if (item is null || !ids.Add(key(item))) return false;
                if (valid != null && !valid(item)) return false;
            }

            return true;
        }

        // AreUnique를 통과한 목록만 읽기 전용 배열로 한 번 복사한다. 실패하면 copy는 null이다.
        internal static bool TryCopyUnique<T, TKey>(IReadOnlyList<T>? source, Func<T, TKey> key,
            [NotNullWhen(true)] out IReadOnlyList<T>? copy, Func<T, bool>? valid = null)
        {
            if (!AreUnique(source, key, valid))
            {
                copy = null;
                return false;
            }

            var items = new T[source.Count];
            for (int index = 0; index < items.Length; index++) items[index] = source[index];
            copy = Array.AsReadOnly(items);
            return true;
        }
    }
}
