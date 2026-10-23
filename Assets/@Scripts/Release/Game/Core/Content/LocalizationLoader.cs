#nullable enable annotations
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Game.Core
{
    // Localization 초기화, 로캘 선택, 문자열 테이블 준비를 Core의 한 경로에서 기다린다.
    // 핸들은 패키지가 소유하므로 기다리기만 하고 반납하지 않는다.
    public static class LocalizationLoader
    {
        public static async UniTask SelectAsync(string localeCode, string table, CancellationToken token = default)
        {
            await LocalizationSettings.InitializationOperation
                .ToUniTask(cancellationToken: token, cancelImmediately: true);

            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(localeCode);
            if (locale == null)
                throw new InvalidOperationException("로캘 코드가 없습니다: " + localeCode);

            StringTable stringTable = await LocalizationSettings.StringDatabase.GetTableAsync(table, locale)
                .ToUniTask(cancellationToken: token, cancelImmediately: true);
            if (stringTable == null)
                throw new InvalidOperationException("번역 테이블이 없습니다: " + table);

            LocalizationSettings.SelectedLocale = locale;
        }
    }
}
