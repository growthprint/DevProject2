#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace DuckCharacter.AvatarParts
{
    /// <summary>
    /// 캐릭터 외형 한 벌을 담는 직렬화 클래스. 씬 컴포넌트를 소유하지 않는다.
    /// 부위별 배열은 DuckAvatarPartType 값이 인덱스이며 손상된 배열은 자동 복구하지 않는다.
    /// 원작 외형 JSON 변환은 DuckovAppearanceJson 만 하고, 표시 이름 같은 로컬 메타데이터는 게임 저장 봉투(GameSaveJson)가 소유한다.
    /// </summary>
    [Serializable]
    public class DuckAvatarPreset
    {

        // 표시 이름은 원작 외형 JSON이 아니라 로컬 저장 메타데이터에서 복원한다.
        [Tooltip("캐릭터 이름. 시작 화면에서 입력한다.")]
        public string _displayName = "";

        // 원작 headSetting에 해당하며, 머리 형태와 피부색을 담는다.
        public DuckHeadSetting _headSetting = new DuckHeadSetting();

        // 부위별 ID와 배치값 배열이며 Unity 직렬화에만 쓴다. 원작 JSON은 DuckovAppearanceJson이 부위별 키로 따로 쓴다.
        [SerializeField]
        int[]? _avatarPartIds;

        [SerializeField]
        DuckAvatarPartInfo[]? _avatarPartInfos;

        // 커스터마이즈 화면 표시 여부도 원작 외형 JSON에 없고 로컬 저장 메타데이터에서 복원한다.
        public bool _showInCustomizationScreen = true;

        /// <summary> 배열을 갖춘 빈 인스턴스.</summary>
        DuckAvatarPreset CreateEmpty()
        {
            DuckAvatarPreset a = new DuckAvatarPreset();
            a._avatarPartIds = new int[(int)DuckAvatarPartType.Count];
            a._avatarPartInfos = new DuckAvatarPartInfo[(int)DuckAvatarPartType.Count];

            for (int i = 0; i < (int)DuckAvatarPartType.Count; i++)
                a._avatarPartInfos[i] = new DuckAvatarPartInfo();

            return a;
        }

        // 기본 외형에서 눈, 눈썹, 부리가 함께 쓰는 반지름과 눈 높이다. 눈썹 높이는 이 눈 높이를 기준으로 잡는다.
        const float _faceRadius = 0.23f;
        const float _defaultEyeHeight = 0.08915874f;

        // 수치는 원본 CustomFacePreset_Default를 따르고, 눈썹 간격 50과 눈썹 상대 높이 0.08만 원본과 다르다.
        // 원본 파츠 id는 모두 0이라 눈도 부리도 없는 오리가 되므로, 눈과 부리와 꼬리는 실제로 보이는 파츠 id를 쓴다.
        /// <summary>플레이어 기본 외형을 만든다.</summary>
        public DuckAvatarPreset CreateDefault()
        {
            DuckAvatarPreset a = CreateEmpty();

            a.SetID(DuckAvatarPartType.Hair, 0);
            a.SetInfo(DuckAvatarPartType.Hair, new DuckAvatarPartInfo(0f, new Color(1f, 0.3146315f, 0.17647058f, 1f), 0f, 0f, 1f, 0f, 0f, 0f));

            a.SetID(DuckAvatarPartType.Eye, 1);      // 검은 눈알 (id 0 은 "없음")
            a.SetInfo(DuckAvatarPartType.Eye, new DuckAvatarPartInfo(_faceRadius, Color.black, _defaultEyeHeight, 0f, 1.1f, 0f, 49.57454f, 0f));

            a.SetID(DuckAvatarPartType.Eyebrow, 0);
            a.SetInfo(DuckAvatarPartType.Eyebrow, new DuckAvatarPartInfo(_faceRadius, Color.black, 0.08f, _defaultEyeHeight, 1f, 0f, 50f, 0f));

            a.SetID(DuckAvatarPartType.Mouth, 2);    // 기본 오리 부리
            a.SetInfo(DuckAvatarPartType.Mouth, new DuckAvatarPartInfo(_faceRadius, new Color(1f, 0.53376853f, 0.4009434f, 1f), 0.037396934f, 0f, 0.73720145f, 0f, 0f, 0f));

            a.SetID(DuckAvatarPartType.Tail, 1);     // 기본 꼬리깃
            a.SetInfo(DuckAvatarPartType.Tail, new DuckAvatarPartInfo(0f, new Color(1f, 0.73487306f, 0.23113209f, 1f), 0f, 0f, 1f, 0f, 0f, 0f));

            a.SetID(DuckAvatarPartType.Foot, 0);
            a.SetInfo(DuckAvatarPartType.Foot, new DuckAvatarPartInfo(0f, new Color(1f, 0.40317395f, 0.2028302f, 1f), 0f, 0f, 1f, 0f, 0f, 0f));

            a.SetID(DuckAvatarPartType.Wing, 0);
            a.SetInfo(DuckAvatarPartType.Wing, new DuckAvatarPartInfo(0f, Color.white, 0f, 0f, 1f, 0f, 0f, 0f));

            return a;
        }

        // ---------------------------------------------------------------- 카테고리 접근

        // 외형은 CreateDefault나 검증된 판독과 복제로 배열을 갖추므로 접근자는 부위 범위와 배열 구조를 다시 보지 않는다.
        // 호출자는 0부터 DuckAvatarPartType.Count 앞까지의 부위만 넘기고 설정 슬롯에 null을 넣지 않는다.
        public int GetID(DuckAvatarPartType type) => _avatarPartIds![(int)type];

        public void SetID(DuckAvatarPartType type, int id)
        {
            _avatarPartIds![(int)type] = id;
        }

        /// <summary>현재 파츠 설정 참조를 반환한다. 독립 편집에는 Clone 또는 편집 세션을 사용한다.</summary>
        public DuckAvatarPartInfo GetInfo(DuckAvatarPartType type) => _avatarPartInfos![(int)type];

        public void SetInfo(DuckAvatarPartType type, DuckAvatarPartInfo info)
        {
            _avatarPartInfos![(int)type] = info;
        }

        public void SetColor(DuckAvatarPartType type, Color color)
        {
            _avatarPartInfos![(int)type]._color = color;
        }

        // ---------------------------------------------------------------- 복제 / 검증

        // 입력 검증을 통과한 외형을 구조와 값 재검사 없이 깊게 복제한다. 늘 새 인스턴스에 복사하므로 자기 복사가 생기지 않는다.
        public DuckAvatarPreset Clone()
        {
            DuckAvatarPreset a = new DuckAvatarPreset();
            a.CopyValidated(this);
            return a;
        }

        // UI나 JSON에서 온, 신뢰할 수 없는 외형을 복제할 때 쓴다. 검증에 실패하면 기록 없이 false만 돌려주고, 보고는 호출자가 맡는다.
        public bool TryClone([NotNullWhen(true)] out DuckAvatarPreset? clone)
        {
            if (!TryValidate())
            {
                clone = null;
                return false;
            }

            clone = new DuckAvatarPreset();
            clone.CopyValidated(this);
            return true;
        }

        // 검증을 통과했거나 신뢰된 원본만 받아 배열을 깊게 복사한다.
        void CopyValidated(DuckAvatarPreset other)
        {
            int[] copiedIDs = new int[(int)DuckAvatarPartType.Count];
            DuckAvatarPartInfo[] copiedInfos = new DuckAvatarPartInfo[(int)DuckAvatarPartType.Count];

            for (int i = 0; i < (int)DuckAvatarPartType.Count; i++)
            {
                copiedIDs[i] = other._avatarPartIds![i];
                copiedInfos[i] = other._avatarPartInfos![i].Clone();
            }

            // 머리까지 모두 복제한 뒤에만 필드를 교체해 참조를 공유하지 않고 중간 실패가 대상을 바꾸지 않게 한다.
            DuckHeadSetting copiedHead = other._headSetting.Clone();
            _headSetting = copiedHead;
            _showInCustomizationScreen = other._showInCustomizationScreen;
            _displayName = other._displayName;
            _avatarPartIds = copiedIDs;
            _avatarPartInfos = copiedInfos;
        }

        // 값은 보지 않고 null 여부와 배열 길이만 할당 없이 검사한다. TryValidate가 값 검사 전에 부른다.
        // 기록 없이 bool만 돌려주고, 보고는 호출자가 맡는다.
        internal bool CheckShape()
        {
            if (_headSetting == null)
                return false;

            if (_avatarPartIds == null || _avatarPartIds.Length != (int)DuckAvatarPartType.Count)
                return false;

            if (_avatarPartInfos == null || _avatarPartInfos.Length != (int)DuckAvatarPartType.Count)
                return false;

            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
            {
                if (_avatarPartInfos[(int)type] == null)
                    return false;
            }

            return true;
        }

        // 원작 JSON 판독, 저장 불러오기, 편집 세션처럼 외부에서 외형이 들어오는 곳에서 쓴다. 구조를 본 뒤 머리와 부위 배치값이
        // 각자 가진 값 검사로 모든 수치와 색이 유한한지 본다. UI 범위나 파츠 ID는 보정하지 않으며, 기록 없이 bool만 돌려주고 보고는 호출자가 맡는다.
        public bool TryValidate()
        {
            if (!CheckShape())
                return false;

            if (!_headSetting.IsFinite())
                return false;

            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
            {
                if (!_avatarPartInfos![(int)type].IsFinite())
                    return false;
            }

            return true;
        }
    }
}
