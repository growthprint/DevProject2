#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Game.App
{
    // 번역 테이블과 키는 표시 계층만 안다. 로캘 선택은 Unity Localization에 맡기고, 앱 시작 때 시스템 언어를 한 번 적용한다.
    public static class GameLocalization
    {
        public static string Table => "GameUI";

        // 원본의 키보드와 마우스 제어 체계 그룹 이름이다. 키캡 표시에 쓴다.
        const string _keyboardMouseGroup = "Keyboard&Mouse";
        // 지원 로캘 코드다. 옵션 검증과 옵션 화면도 이 값을 써서 로캘 자산 코드와 철자가 어긋나지 않게 한다.
        internal const string _koreanLocale = "ko-KR";
        internal const string _englishLocale = "en";
        // 원작 번역 표의 키 형식이다. 설명 키는 이름 키 끝에, 스탯과 태그 이름 키는 원작 키 앞에 붙인다.
        const string _descriptionKeySuffix = "_Desc";
        const string _statNamePrefix = "Stat_";
        const string _tagNamePrefix = "Tag_";
        // 원작 알림 문구에서 아이템 표시 이름이 들어갈 자리다.
        const string _itemNamePlaceholder = "{itemDisplayName}";

        // 로캘마다 한 번 찾은 앱 문자열이다. 목록 화면이 칸과 줄마다 같은 키를 다시 찾지 않게 한다(Docs/runtime-boundaries.md#main-thread-split).
        // 앱 표(GameUI)에는 스마트 문자열이 없어(2026-10-22 확인) 같은 로캘의 같은 키는 늘 같은 글자다. 메인 스레드에서만 읽고 쓴다.
        static readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        static Locale? _textsLocale;

        // Domain Reload를 끄면 정적 값이 Play 세션을 넘어 남으므로, 그 사이 고친 번역을 놓치지 않게 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTexts()
        {
            _texts.Clear();
            _textsLocale = null;
        }

        // 현재 로캘의 앱 문자열을 읽는 유일한 경로다. 게임플레이와 UI는 따로 래퍼를 두지 않는다.
        // 로캘이 바뀌었으면 처음 읽을 때 이전 로캘의 글자를 비운다.
        public static string Text(string key)
        {
            Locale locale = LocalizationSettings.SelectedLocale;
            if (!ReferenceEquals(locale, _textsLocale))
            {
                _texts.Clear();
                _textsLocale = locale;
            }
            if (_texts.TryGetValue(key, out string text)) return text;
            text = LocalizationSettings.StringDatabase.GetLocalizedString(Table, key);
            _texts[key] = text;
            return text;
        }

        // 원작 ToPlainText처럼 비어 있는 키는 번역을 찾지 않고 빈 글자다. 설명 키가 빌 수 있는 퀘스트, 도감, 재능 화면이 쓴다.
        internal static string TextOrEmpty(string key) => key.Length == 0 ? "" : Text(key);

        // 원작 설명 번역 키는 이름 키 끝에 "_Desc"를 더한다. 난이도, 재능, 특성, 건물, 아이템 설명이 이 형식을 함께 쓴다.
        internal static string DescriptionKey(string nameKey) => nameKey + _descriptionKeySuffix;

        // 원작 스탯 표시 이름 키("Stat_" + 스탯 키)와 태그 표시 이름 키("Tag_" + 태그 이름)다. 아이템 정보, 재능, 특성 화면이 함께 쓴다.
        internal static string StatNameKey(string stat) => _statNamePrefix + stat;
        internal static string TagNameKey(string tag) => _tagNamePrefix + tag;

        // 원작 알림 문구의 {itemDisplayName} 자리에 아이템 이름을 넣는다. 자리가 없는 문구는 그대로 돌려준다.
        internal static string WithItemName(string format, string itemName) => format.Replace(_itemNamePlaceholder, itemName);

        // 원본 난이도 번역 키는 "Rule_" 뒤에 규칙 열거형 이름을 붙이고, 설명 키는 그 끝에 "_Desc"를 더한다.
        // 옵션 창 배지, 저장 칸, 난이도 화면이 이 형식을 함께 쓴다.
        internal static string DifficultyNameKey(DifficultyRuleIndex rule) => "Rule_" + rule;
        internal static string DifficultyDescriptionKey(DifficultyRuleIndex rule) => DescriptionKey(DifficultyNameKey(rule));

        // 원본 InputIndicator 키캡처럼 키보드나 마우스 바인딩 표기만 만든다. 재바인딩한 경로를 따르고, 없으면 빈 문자열이다.
        // Keyboard&Mouse 그룹을 먼저 보고, 그룹 없이 저작된 <Keyboard>나 <Mouse> 바인딩(Reload, QuickSlot1 등)도 받는다.
        public static string KeyboardBindingLabel(InputAction action)
        {
            string grouped = action.GetBindingDisplayString(InputBinding.MaskByGroup(_keyboardMouseGroup));
            if (!string.IsNullOrEmpty(grouped))
                return grouped;

            var bindings = action.bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                InputBinding binding = bindings[index];
                if (binding.isPartOfComposite || !string.IsNullOrEmpty(binding.groups))
                    continue;
                string path = (binding.isComposite && index + 1 < bindings.Count ? bindings[index + 1].effectivePath :
                    binding.effectivePath) ?? "";
                if (path.StartsWith("<Keyboard>", StringComparison.Ordinal) || path.StartsWith("<Mouse>", StringComparison.Ordinal))
                    return action.GetBindingDisplayString(index);
            }
            return "";
        }

        // 한국어 계열 지역은 한국어로, 나머지는 지원하는 영어로 시작한다. 결과는 늘 지원 로캘 상수 중 하나다.
        public static string ResolveLocaleCode(string? cultureName, SystemLanguage language) =>
            language == SystemLanguage.Korean || IsKoreanCulture(cultureName) ? _koreanLocale : _englishLocale;

        // "ko"와 "ko-KR" 같은 한국어 문화권 이름이면 true다. 비었거나 없으면 false다.
        static bool IsKoreanCulture(string? cultureName) =>
            !string.IsNullOrEmpty(cultureName) &&
            (cultureName.Equals("ko", StringComparison.OrdinalIgnoreCase) || cultureName.StartsWith("ko-", StringComparison.OrdinalIgnoreCase));

        // 앱 시작에서 화면 생성 전에 시스템 언어의 로캘을 선택한다. 최초 언어와 저장된 언어 변경이 같은 SelectAsync 경로를 쓴다.
        public static async UniTask InitializeAsync(CancellationToken token)
        {
            string code = ResolveLocaleCode(CultureInfo.CurrentUICulture.Name, Application.systemLanguage);
            await SelectAsync(code, token);
        }

        // 앱 테이블 이름만 넘기고 실제 선택은 LocalizationLoader가 맡는다. 초기화와 테이블 준비가 끝나야 언어를 바꾸므로,
        // 실패해도 현재 언어는 그대로다. 실패는 예외로 호출자에게 전달된다.
        public static UniTask SelectAsync(string code,CancellationToken token) =>
            LocalizationLoader.SelectAsync(code, Table,token);
    }
}
