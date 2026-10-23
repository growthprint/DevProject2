#nullable enable


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using UnityEngine;

namespace DuckCharacter.AvatarParts
{
    // 필드 구성과 JSON 키는 원본 CustomFace의 partInfo와 같다. 부위는 담지 않고 DuckAvatarPreset 배열의 인덱스가 정한다.
    /// <summary>파츠 프리팹 위에 얹는 절차적 배치값이다.</summary>
    [Serializable]
    public class DuckAvatarPartInfo
    {
        [JsonProperty("radius")]
        [Tooltip("머리 구(球) 표면에 배치할 때 쓰는 반지름. 중앙 파츠와 좌우 한 쌍 파츠 모두 배치 방향으로 이만큼 떨어진다. 0이면 높이(height·heightOffset)만 적용한다.")]
        public float _radius;

        [JsonProperty("color")]
        [Tooltip("파츠 틴트 색. customColorKey 로 지정한 셰이더 프로퍼티에 들어간다.")]
        public Color _color = Color.white;

        [JsonProperty("height")]
        [Tooltip("소켓 기준 상하 오프셋.")]
        public float _height;

        [JsonProperty("heightOffset")]
        [Tooltip("height 에 추가로 더해지는 오프셋. 눈썹을 눈 위로 밀어올릴 때처럼 상대 배치에 쓴다.")]
        public float _heightOffset;

        [JsonProperty("scale")]
        [Tooltip("파츠 전체 스케일 배수.")]
        public float _scale = 1f;

        [JsonProperty("twist")]
        [Tooltip("파츠를 자기 축으로 비트는 각(도).")]
        public float _twist;

        [JsonProperty("distanceAngle")]
        [Tooltip("좌우 한 쌍 파츠가 정면 기준으로 벌어지는 각(도).")]
        public float _distanceAngle;

        [JsonProperty("leftRightAngle")]
        [Tooltip("좌우 한 쌍 전체를 좌우로 함께 돌리는 각(도).")]
        public float _leftRightAngle;

        // 0.0001 이하로 저장된 배율은 파츠가 사라지지 않도록 1로 대체한다.
        public float SafeScale { get { return _scale <= 0.0001f ? 1f : _scale; } }

        public DuckAvatarPartInfo() { }

        // 원작 partInfo 의 8개 배치값을 받는다.
        public DuckAvatarPartInfo(float radius, Color color, float height, float heightOffset,
            float scale, float twist, float distanceAngle, float leftRightAngle)
        {
            _radius = radius;
            _color = color;
            _height = height;
            _heightOffset = heightOffset;
            _scale = scale;
            _twist = twist;
            _distanceAngle = distanceAngle;
            _leftRightAngle = leftRightAngle;
        }

        // 모든 필드가 값 형식이라 얕은 복사가 곧 독립 사본이다. 참조 형식 필드를 더할 때만 여기서 따로 복제한다.
        public DuckAvatarPartInfo Clone() => (DuckAvatarPartInfo)MemberwiseClone();

        // 외형 검증이 쓰는 값 검사라 필드 선언 옆에 둔다. 필드를 더하면 이 목록과 원작 JSON 키 목록을 함께 맞춘다.
        internal bool IsFinite() =>
            float.IsFinite(_radius) && float.IsFinite(_height) && float.IsFinite(_heightOffset) && float.IsFinite(_scale)
            && float.IsFinite(_twist) && float.IsFinite(_distanceAngle) && float.IsFinite(_leftRightAngle) && IsFinite(_color);

        // 파츠 색과 머리 피부색이 함께 쓰는 색 검사다.
        internal static bool IsFinite(Color color) =>
            float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b) && float.IsFinite(color.a);
    }

    // 원작 headSetting 저장값(JSON 키는 원작 이름)이다. 파츠 배치와 따로 복제한다.
    [Serializable]
    public sealed class DuckHeadSetting
    {
        // 피부색. 머리와 몸통에 공통으로 적용한다.
        [JsonProperty("mainColor")]
        public Color _mainColor = Color.white;

        // 원작 저장값은 표시 배율에서 1을 뺀 가감치다.
        [JsonProperty("headScaleOffset")]
        public float _headScaleOffset;

        // 이마 본 기본 높이에 더하는 값이다. 기본 0.07 은 기본 프리셋의 이마 높이다.
        [JsonProperty("foreheadHeight")]
        public float _foreheadHeight = 0.07f;

        // 이마 본과 헤어 소켓의 Y 스케일이다. 기본 1 은 본 원래 모양이며 저장값은 UI 범위로 보정하지 않는다.
        [JsonProperty("foreheadRound")]
        public float _foreheadRound = 1f;

        // 화면에 보이는 머리 배율과 저장하는 가감치 사이의 변환이다. 편집 저장, 화면 표시, 리그 적용이 모두 이 두 함수를 쓴다.
        public static float ToHeadScale(float headScaleOffset) => 1f + headScaleOffset;
        public static float ToHeadScaleOffset(float headScale) => headScale - 1f;

        // 복제된 외형의 머리 편집이 원본 데이터로 전파되지 않게 한다. 모든 필드가 값 형식이라 얕은 복사가 곧 독립 사본이다.
        public DuckHeadSetting Clone() => (DuckHeadSetting)MemberwiseClone();

        // 외형 검증이 쓰는 값 검사라 필드 선언 옆에 둔다. 필드를 더하면 원작 JSON 키 목록도 함께 맞춘다.
        internal bool IsFinite() =>
            float.IsFinite(_headScaleOffset) && float.IsFinite(_foreheadHeight) && float.IsFinite(_foreheadRound)
            && DuckAvatarPartInfo.IsFinite(_mainColor);
    }

    // 부위와 머리가 공유하는 편집 항목이다. Round 는 머리 전용이다.
    public enum DuckAvatarParameter { Scale, Height, DistanceAngle, Twist, LeftRightAngle, Round }

    // 원작의 부위별 조절 항목과 범위, 머리 범위를 한 곳에 둔다. 저장 데이터를 복원할 때는 이 범위를 강제하지 않는다.
    /// <summary>UI와 편집 세션이 함께 쓰는 읽기 전용 조절 규칙이다.</summary>
    public sealed class DuckAvatarPartLimitRange
    {
        // 값 형식이라 반환된 범위는 복사본이다.
        public struct Limit
        {
            public float _min, _max;
            public Limit(float min, float max) { _min = min; _max = max; }
            public float Clamp(float v) { return Mathf.Clamp(v, _min, _max); }
        }

        // 항목별 범위와 표시 순서다. 외부에 가변 컬렉션을 노출하지 않는다.
        readonly Dictionary<DuckAvatarParameter, Limit> _limits;
        public IReadOnlyList<DuckAvatarParameter> Parameters { get; }

        // 독립 색 선택 지원 여부다. 원작에서 발만 지원하지 않는다.
        public bool SupportsColor { get; }

        // DuckAvatarPartType 값을 인덱스로 찾는 규칙 표다.
        static readonly DuckAvatarPartLimitRange[] _rules = CreateRules();

        // 눈, 눈썹, 부리가 함께 쓰는 원작의 크기, 높이, 비틀기 범위다. 정적 필드 초기화 순서에 영향받지 않도록 const로 둔다.
        const float _faceScaleMin = 0.3f, _faceScaleMax = 4f;
        const float _faceHeightMin = -0.3f, _faceHeightMax = 0.3f;
        const float _faceTwistMin = -90f, _faceTwistMax = 90f;

        // 부위별 원작 범위와 표시 순서다. 눈썹은 눈 규칙을 공유한다.
        static DuckAvatarPartLimitRange[] CreateRules()
        {
            var table = new DuckAvatarPartLimitRange[(int)DuckAvatarPartType.Count];

            // 헤어 형태는 머리 설정을 따르므로 독립 숫자 조절 없이 색상만 노출한다.
            table[(int)DuckAvatarPartType.Hair] = new DuckAvatarPartLimitRange(true);

            table[(int)DuckAvatarPartType.Eye] = new DuckAvatarPartLimitRange(true,
                (DuckAvatarParameter.Scale, _faceScaleMin, _faceScaleMax), (DuckAvatarParameter.Height, _faceHeightMin, _faceHeightMax),
                (DuckAvatarParameter.DistanceAngle, 0f, 90f), (DuckAvatarParameter.Twist, _faceTwistMin, _faceTwistMax));

            table[(int)DuckAvatarPartType.Eyebrow] = table[(int)DuckAvatarPartType.Eye];

            table[(int)DuckAvatarPartType.Mouth] = new DuckAvatarPartLimitRange(true,
                (DuckAvatarParameter.Scale, _faceScaleMin, _faceScaleMax), (DuckAvatarParameter.Height, _faceHeightMin, _faceHeightMax),
                (DuckAvatarParameter.LeftRightAngle, -50f, 50f), (DuckAvatarParameter.Twist, _faceTwistMin, _faceTwistMax));

            table[(int)DuckAvatarPartType.Tail] = new DuckAvatarPartLimitRange(true,
                (DuckAvatarParameter.Scale, 0.3f, 2f));

            table[(int)DuckAvatarPartType.Foot] = new DuckAvatarPartLimitRange(false,
                (DuckAvatarParameter.Scale, 0.5f, 1.5f));

            table[(int)DuckAvatarPartType.Wing] = new DuckAvatarPartLimitRange(true,
                (DuckAvatarParameter.Scale, 0.5f, 2f));

            return table;
        }

        // 입력을 복사해 고정 규칙을 만들고 가변 컬렉션을 노출하지 않는다.
        DuckAvatarPartLimitRange(bool supportsColor,
            params (DuckAvatarParameter parameter, float min, float max)[] entries)
        {
            SupportsColor = supportsColor;
            _limits = new Dictionary<DuckAvatarParameter, Limit>(entries.Length);
            DuckAvatarParameter[] parameters = new DuckAvatarParameter[entries.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                parameters[i] = entries[i].parameter;
                _limits.Add(entries[i].parameter, new Limit(entries[i].min, entries[i].max));
            }
            Parameters = Array.AsReadOnly(parameters);
        }

        // 호출자는 0부터 DuckAvatarPartType.Count 앞까지의 부위만 넘기므로 규칙 표를 바로 읽는다.
        public static DuckAvatarPartLimitRange For(DuckAvatarPartType type)
        {
            return _rules[(int)type];
        }

        // 머리가 지원하는 크기, 높이, 둥글기 항목의 표시 순서다. 커스터마이징 화면과 편집 세션이 같은 목록을 쓴다.
        static readonly ReadOnlyCollection<DuckAvatarParameter> _headParameters = Array.AsReadOnly(new[]
            { DuckAvatarParameter.Scale, DuckAvatarParameter.Height, DuckAvatarParameter.Round });

        public static IReadOnlyList<DuckAvatarParameter> HeadParameters => _headParameters;

        // 머리 세 항목의 표시 범위를 조회한다. 머리가 지원하지 않는 항목은 기록 없이 false고, GetHeadLimit이 이를 빈 범위로 바꾼다.
        public static bool TryGetHeadLimit(DuckAvatarParameter parameter, out Limit limit)
        {
            switch (parameter)
            {
                case DuckAvatarParameter.Scale: limit = new Limit(0.6f, 1.4f); return true;
                case DuckAvatarParameter.Height: limit = new Limit(0f, 0.6f); return true;
                case DuckAvatarParameter.Round: limit = new Limit(0.35f, 1f); return true;
                default: limit = default; return false;
            }
        }

        // HeadParameters로 화면을 구성하는 호출자와 머리 수치 편집이 쓰는 범위 조회다. 머리에 없는 항목은 빈 범위 0~0이다.
        public static Limit GetHeadLimit(DuckAvatarParameter parameter)
        {
            TryGetHeadLimit(parameter, out Limit limit);
            return limit;
        }

        // 지원 여부와 범위를 한 번에 조회하여 UI와 입력 검증이 일치하게 한다.
        public bool TryGetLimit(DuckAvatarParameter parameter, out Limit limit)
        {
            return _limits.TryGetValue(parameter, out limit);
        }
    }
}
