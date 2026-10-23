#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using DevProject.Serialization;
using Game.Core;
using UnityEngine;

namespace DuckCharacter.AvatarParts
{
    // 모든 변경은 프리셋이나 미리보기와 참조를 공유하지 않는 복사본에서 만든 뒤 한 번에 확정한다.
    /// <summary>
    /// 화면과 분리된 외형 편집 세션이다. 숫자 입력은 문화권과 관계없이 점을 소수점으로 읽는다.
    /// 미리보기는 Changed를 구독한다. 클립보드는 UI가 다루고, 세션은 원작 JSON 변환만 한다.
    /// </summary>
    public sealed class DuckCustomizationSession
    {
        // 파츠와 팔레트를 고를 때 기준이 되는 데이터다.
        readonly DuckAvatarPartLibrary _library;

        // 원작 JSON 변환은 주입받아 세션과 따로 교체할 수 있다.
        readonly IJsonCodec<DuckAvatarPreset> _originalJson;
        // 편집 권한은 호스트가 쥔다. 잠기면 세션 참조를 가진 쪽도 다시 열거나 바꾸지 못한다.
        readonly Func<bool>? _canEdit;

        // 현재 외형이다. 밖으로는 복사본만 내보낸다.
        DuckAvatarPreset _current;

        // 알림 도중 생긴 변경도 확정 순서대로 전달해, 뒤쪽 구독자가 마지막에 낡은 외형을 받지 않게 한다.
        readonly Queue<DuckAvatarPreset> _pendingChanges = new Queue<DuckAvatarPreset>();

        bool _publishingChanges;

        public DuckAvatarPartLibrary Library => _library;

        /// <summary>
        /// 구독자마다 별도의 깊은 복사본을 받는다. 구독자 예외는 호출자에게 던지지 않고 세션이 한 번씩 기록하며,
        /// 다른 구독자와 대기 중인 확정 알림을 모두 전달한다.
        /// </summary>
        public event Action<DuckAvatarPreset>? Changed;

        // 편집 씬 준비(CustomizationSceneContext.Prepare)만 세션을 만든다. 시작 외형은 ID를 보정한 복사본으로 쓴다.
        public DuckCustomizationSession(DuckAvatarPartLibrary library, DuckAvatarPreset initial,
            IJsonCodec<DuckAvatarPreset> originalJson, Func<bool>? canEdit = null)
        {
            _library = library!;
            _originalJson = originalJson!;
            // canEdit이 없으면 늘 편집을 허용한다. 제품 흐름은 편집 잠금을 주입한다.
            _canEdit = canEdit;
            _current = PrepareCopy(initial);
        }

        // 미리보기와 화면에는 늘 복사본을 준다. 받은 쪽이 고쳐도 세션 외형은 그대로다.
        public DuckAvatarPreset Snapshot() { return _current.Clone(); }

        // 없는 ID는 라이브러리가 보정한다. 잠긴 편집은 기록 없이 false다.
        public bool SelectPart(DuckAvatarPartType type, int id)
        {
            if (!CanEdit) return false;
            return Change(candidate => candidate.SetID(type, id));
        }

        // 슬라이더는 부위별 제한표에 있는 항목만 편집하고, 값은 범위 안으로 자른다. 잠긴 편집은 기록 없이 false다.
        // 화면은 제한표 항목에만 슬라이더와 문자 입력을 연결하고, 문자 입력은 TrySetValue가 거른 유한한 값만 넘기므로 조회 결과를 다시 보지 않는다.
        public bool SetValue(DuckAvatarPartType type, DuckAvatarParameter parameter, float value)
        {
            if (!CanEdit) return false;
            DuckAvatarPartLimitRange.For(type).TryGetLimit(parameter, out DuckAvatarPartLimitRange.Limit limit);
            float clamped = limit.Clamp(value);
            return Change(candidate => SetPartValue(candidate.GetInfo(type), parameter, clamped));
        }

        // 직접 입력도 슬라이더와 같은 범위로 자른다. 잘못된 문자 입력은 기록 없이 false다. 항목은 화면이 제한표에서만 고른다.
        public bool TrySetValue(DuckAvatarPartType type, DuckAvatarParameter parameter, string value)
        {
            // 편집 잠금은 SetValue가 기록 없이 거부하므로 여기서 다시 보지 않는다.
            return TryParseNumber(value, out float number) && SetValue(type, parameter, number);
        }

        // 머리도 같은 조절 항목 enum을 쓰고, 표시 배율만 저장용 가감치로 바꾼다. 잠긴 편집은 기록 없이 false다.
        // 화면은 HeadParameters 항목의 슬라이더 값만, 문자 입력은 TrySetHeadValue가 거른 유한한 값만 넘긴다.
        public bool SetHeadValue(DuckAvatarParameter parameter, float value)
        {
            if (!CanEdit) return false;
            DuckAvatarPartLimitRange.Limit limit = DuckAvatarPartLimitRange.GetHeadLimit(parameter);
            float clamped = limit.Clamp(value);
            // 저장 필드 대응은 아래 대응 함수 묶음이 읽기와 함께 맡는다.
            return Change(candidate => SetHeadSettingValue(candidate._headSetting, parameter, clamped));
        }

        // 잘못된 문자 입력은 기록 없이 false다. 항목은 화면이 HeadParameters에서만 고른다.
        public bool TrySetHeadValue(DuckAvatarParameter parameter, string value)
        {
            // 편집 잠금은 SetHeadValue가 기록 없이 거부하므로 여기서 다시 보지 않는다.
            return TryParseNumber(value, out float number) && SetHeadValue(parameter, number);
        }

        // 원본처럼 부위 색도 피부색과 같은 공통 팔레트에서만 고른다. 잠긴 편집은 기록 없이 false다.
        // 화면은 독립 색 선택을 지원하는 부위에만 색 선택을 연결한다.
        public bool SelectColor(DuckAvatarPartType type, int paletteIndex)
        {
            if (!CanEdit) return false;
            Color color = ReadCommonColor(paletteIndex);
            return Change(candidate => candidate.SetColor(type, color));
        }

        // 피부색은 공통 팔레트에서 고르고 알파는 불투명으로 고정한다. 잠긴 편집은 기록 없이 false다.
        public bool SelectMainColor(int paletteIndex)
        {
            if (!CanEdit) return false;
            Color color = ReadCommonColor(paletteIndex);
            return Change(candidate => candidate._headSetting._mainColor = color);
        }

        // 라이브러리 프리셋 원본이 바뀌지 않도록 깊은 복사본을 적용한다. 잠긴 편집은 기록 없이 false다.
        public bool ApplyPreset(DuckAvatarPreset appearance)
        {
            if (!CanEdit) return false;
            return Publish(PrepareCopy(appearance));
        }

        // 원본 YellowDuck 버튼처럼 임의 수치가 아니라 라이브러리에 등록된 프리셋 하나를 고른다.
        // 잠긴 편집은 난수를 쓰지 않고 false다. 프리셋 목록이 비면 화면이 버튼을 끈다.
        public bool ApplyRandomPreset(System.Random random)
        {
            if (!CanEdit) return false;
            IReadOnlyList<DuckAvatarPreset> presets = _library.Presets;
            return ApplyPreset(presets[random.Next(presets.Count)]);
        }

        // 원작 JSON을 가져온다. 잠긴 편집은 기록 없이 false고, 실패하면 현재 외형을 그대로 둔다.
        // 손상 입력은 여기서 InvalidData로 한 번 보고한다. 에디터와 DEBUG 빌드는 예외로 멈추고, 릴리즈는 Warning 뒤 false다.
        public bool TryImportOriginalJson(string? json)
        {
            if (!CanEdit) return false;
            // null 입력은 코덱이 빈 입력으로 거부한다. TryRead가 성공한 값은 null이 아니고 검증도 통과했으므로 다시 검증하지 않는다.
            if (!_originalJson.TryRead(json!, out DuckAvatarPreset? appearance, out string readError))
                return Log.Instance.InvalidData(new FormatException(readError));

            // 머리값과 피부색, 부위 값은 원작 JSON을 따르고, 원작 JSON에 없는 이름과 프리셋 표시 여부는 현재 값을 지킨다.
            // 코덱이 돌려준 객체를 나중에 고쳐도 세션이 바뀌지 않도록 한 번 깊게 복사한다.
            DuckAvatarPreset merged = appearance.Clone();
            merged._displayName = _current._displayName;
            merged._showInCustomizationScreen = _current._showInCustomizationScreen;
            // 검증을 거친 값이라 다시 검증하지 않고 ID만 보정한다. Sanitize는 정수 ID만 바꾼다.
            _library.Sanitize(merged);

            return Publish(merged);
        }

        // 주입된 변환기에도 복사본만 넘겨 현재 외형을 보호한다.
        public string ExportOriginalJson(bool pretty = false) { return _originalJson.Write(Snapshot(), pretty); }

        // 복사본에서만 값을 바꿔, 편집이 실패해도 현재 외형은 그대로다. 편집 잠금은 공개 명령이 들어올 때 한 번만 본다.
        // 입력 수치는 유한하고 범위 안이며 팔레트 색은 제작 데이터라 다시 검증하지 않고 ID만 보정한다.
        bool Change(Action<DuckAvatarPreset> edit)
        {
            DuckAvatarPreset candidate = _current.Clone();
            edit(candidate);
            _library.Sanitize(candidate);
            return Publish(candidate);
        }

        // 시작 외형은 검증된 외형 소유자의 값이고 프리셋은 제작 데이터라, 다시 검증하지 않고 복제한 뒤 ID만 보정한다.
        DuckAvatarPreset PrepareCopy(DuckAvatarPreset source)
        {
            DuckAvatarPreset copy = source.Clone();
            _library.Sanitize(copy);
            return copy;
        }

        // 알림 도중 다시 확정되거나 구독자가 예외를 던져도, 모든 확정 외형을 순서대로 전달한다. 결과는 늘 true다.
        // 공개 명령의 잠금 검사부터 여기까지는 동기 계산만 돌고, 주입 코덱도 잠금을 바꾸지 않는다는 전제라 다시 보지 않는다.
        bool Publish(DuckAvatarPreset candidate)
        {
            _current = candidate;
            _pendingChanges.Enqueue(candidate);
            // 알림 도중 들어온 확정은 재귀로 알리지 않고 대기열에만 넣는다. 바깥 루프가 순서대로 전달한다.
            if (_publishingChanges) return true;
            _publishingChanges = true;
            try
            {
                while (_pendingChanges.Count > 0)
                {
                    DuckAvatarPreset committed = _pendingChanges.Dequeue();
                    // 구독자마다 새 복사본을 만들어 넘긴다. 예외를 던진 구독자는 Log.Instance.Exception으로 기록하고 다음 구독자를 계속 부른다.
                    ObserverDispatch.InvokeEach(Changed, committed.Clone);
                }
            }
            finally
            {
                _publishingChanges = false;
            }
            return true;
        }

        bool CanEdit => _canEdit == null || _canEdit();

        // 문화권과 관계없이 점을 소수점으로 읽고, NaN과 무한대는 거절한다.
        static bool TryParseNumber(string value, out float number)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !float.IsNaN(number) && !float.IsInfinity(number);
        }

        // UI 조절 항목과 저장 필드의 대응은 아래 네 함수에만 둔다. 세션 편집이 쓰고, 화면 슬라이더 갱신이 읽는다.
        static void SetPartValue(DuckAvatarPartInfo info, DuckAvatarParameter parameter, float value)
        {
            switch (parameter)
            {
                case DuckAvatarParameter.Scale:
                    info._scale = value;
                    break;
                case DuckAvatarParameter.Height:
                    info._height = value;
                    break;
                case DuckAvatarParameter.DistanceAngle:
                    info._distanceAngle = value;
                    break;
                case DuckAvatarParameter.Twist:
                    info._twist = value;
                    break;
                case DuckAvatarParameter.LeftRightAngle:
                    info._leftRightAngle = value;
                    break;
            }
        }

        // 부위가 지원하는 조절 항목의 저장값을 읽는다. 다른 항목은 부위 제한표가 걸러 오지 않으므로 기본 분기는 표시값 0이다.
        internal static float GetPartValue(DuckAvatarPartInfo info, DuckAvatarParameter parameter) => parameter switch
        {
            DuckAvatarParameter.Scale => info._scale,
            DuckAvatarParameter.Height => info._height,
            DuckAvatarParameter.DistanceAngle => info._distanceAngle,
            DuckAvatarParameter.Twist => info._twist,
            DuckAvatarParameter.LeftRightAngle => info._leftRightAngle,
            _ => 0f
        };

        // 화면과 문자 입력이 머리 조절 목록의 세 항목만 넘기며, 다른 항목은 값을 바꾸지 않는다.
        static void SetHeadSettingValue(DuckHeadSetting setting, DuckAvatarParameter parameter, float value)
        {
            switch (parameter)
            {
                case DuckAvatarParameter.Scale: setting._headScaleOffset = DuckHeadSetting.ToHeadScaleOffset(value); break;
                case DuckAvatarParameter.Height: setting._foreheadHeight = value; break;
                case DuckAvatarParameter.Round: setting._foreheadRound = value; break;
            }
        }

        // 머리 조절 항목의 표시값을 읽는다. 다른 항목은 머리 조절 목록이 걸러 오지 않으므로 기본 분기는 표시값 0이다.
        internal static float GetHeadSettingValue(DuckHeadSetting setting, DuckAvatarParameter parameter) => parameter switch
        {
            // 머리 크기는 저장 offset에 1을 더한 배율로 보여 준다. 편집 저장, 리그 적용과 같은 변환 함수를 쓴다.
            DuckAvatarParameter.Scale => DuckHeadSetting.ToHeadScale(setting._headScaleOffset),
            DuckAvatarParameter.Height => setting._foreheadHeight,
            DuckAvatarParameter.Round => setting._foreheadRound,
            _ => 0f
        };

        // 피부색과 부위 색이 함께 쓰는 공통 팔레트에서 읽는다. 팔레트 원본은 건드리지 않고 읽은 색의 알파만 불투명으로 고정한다.
        // 화면은 라이브러리 팔레트의 칸 수만큼만 번호를 넘긴다.
        Color ReadCommonColor(int index)
        {
            Color color = _library.Palette!.Common![index].Color;
            color.a = 1f;
            return color;
        }
    }
}
