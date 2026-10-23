#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 HUD 밖의 월드 표시인 DialogueInteractiveCanvas, DialogueBubbleCanvas, PopText를 대신한다.
    // 대사 진행은 DialogueSequenceRunner가, 결과 화면과 상태 게이지 같은 HUD는 SharedHudView가 맡는다.
    // 앱 구성이 한 번 로드한 인스턴스를 전투 월드나 프롤로그 같은 씬 소유자 하나가 한 번에 붙인다. 붙이고 떼는 소유 규칙은 OwnedAppView가 맡는다.
    public sealed class OriginalGameplayView : OwnedAppView
    {
        // 말풍선을 띄울 대상 위 높이(m)다. 원작 DialogueBubbleProxy.yOffset이고, 플레이어 말풍선은 2m다.
        const float _playerBubbleHeight = 2f;
        // 원본 말풍선이 글자를 다 띄운 뒤 남는 최소 시간(초)과, 화면 높이에 대한 비율로 더 올리는 화면 위치다.
        const float _bubbleMinimumSustain = 2f, _bubbleScreenLift = .06f;
        // 피격 표시의 빨강 값, 마지막 피격 뒤 남는 시간(초), 남은 시간을 알파로 바꾸는 배율이다.
        const float _hitFlashRed = .65f, _hitFlashDuration = .35f, _hitFlashAlphaScale = .6f;
        // 원본 PopText가 떠오르기 시작하는 높이(받은 지점 위 m)와 보이는 시간(초)이다.
        const float _popRise = 2f, _popLifetime = 1f;

        // 아래 참조는 모두 제작 도구가 연결하고 EditMode 저작 검사가 확인하므로 non-null로 두고 null을 검사하지 않는다.
        [SerializeField] GameObject _bubblePanel = null!;
        // 원본 DialogueUI 외형의 공용 대사 상자(UI/Common/DialogueBox)다. 표시와 글자 공개만 하고 진행은 대사 진행자가 맡는다.
        // 숨길 때 자기 루트를 스스로 끄므로 대사 패널 GameObject 참조를 따로 두지 않는다.
        [SerializeField] DialogueBoxView _dialogueBox = null!;
        [SerializeField] TMP_Text _bubbleText = null!, _damageTemplate = null!;
        [SerializeField] Image _hitFlash = null!;
        float _bubbleUntil, _hitUntil;
        // 말풍선에 지금까지 공개한 글자 수다. 원본처럼 글자를 다 띄운 뒤에 유지 시간이 따로 흐른다.
        float _bubbleLetters;
        // 원본 말풍선의 출력 속도인 초당 10글자다. 유지 시간 계산과 글자 공개가 함께 쓴다.
        const float _bubbleLettersPerSecond = 10f;
        Transform? _bubbleTarget;
        // 지금 말풍선의 대상 위 높이(m)다. 띄울 때 받은 값이고 처음과 초기화 뒤에는 플레이어 높이다.
        float _bubbleHeight = _playerBubbleHeight;
        Camera? _worldCamera;
        // 숨긴 문구와 그 문구의 치명 아이콘이다. 아이콘은 템플릿에 자식 Image가 있을 때만 있고, 복제할 때 한 번만 찾는다.
        readonly Stack<(TMP_Text text, Image? icon)> _numberPool = new();
        readonly List<(TMP_Text text, Image? icon, Vector3 point, float start)> _numbers = new();

        // 피해 숫자 템플릿은 복제 원본으로만 쓰므로 대사 상자, 말풍선과 함께 처음부터 숨긴다.
        void Awake()
        {
            _dialogueBox.Hide();
            _bubblePanel.SetActive(false);
            _damageTemplate.gameObject.SetActive(false);
        }

        // 커스터마이징으로 월드가 멈추면 표시 루트를 숨기고 재개 때 다시 켠다. 앱 수명 인스턴스라 씬 루트와 함께 꺼지지 않는다.
        // 호출자는 이 표시를 붙인 소유자일 때만 부르므로 소유를 다시 확인하지 않는다.
        internal void SetSuspended(bool suspended) => gameObject.SetActive(!suspended);

        // 갓 생성한 표시 상태로 되돌린다. 표시 중인 피해 숫자는 파괴하지 않고 풀로 돌려보낸다.
        private protected override void ResetState()
        {
            _dialogueBox.Hide();
            _bubblePanel.SetActive(false);
            _bubbleTarget = null;
            // 다음 말풍선이 앞 물체의 높이를 이어받지 않게 플레이어 높이로 되돌린다.
            _bubbleHeight = _playerBubbleHeight;
            _worldCamera = null;
            _bubbleUntil = 0f;
            _bubbleLetters = 0f;
            for (int i = 0; i < _numbers.Count; i++)
            {
                // 아이콘은 문구의 자식이라 문구와 함께 꺼지고, 다음 Pop이 켤지 다시 정한다.
                var number = _numbers[i];
                number.text.gameObject.SetActive(false);
                _numberPool.Push((number.text, number.icon));
            }
            _numbers.Clear();
            _hitUntil = 0f;
            // 피격 표시는 색을 남긴 채 알파만 0으로 둔다.
            _hitFlash.color = new Color(_hitFlashRed, 0, 0, 0);
        }

        // 전투 월드의 대사 진행자(DialogueSequenceRunner)가 쓰는 공용 대사 상자다.
        internal DialogueBoxView DialogueBox => _dialogueBox;

        // 원본 머리 위 말풍선은 하단 대사 상자와 따로 정해진 시간만 보인다. 글자를 다 띄운 뒤 최소 유지 시간만큼 남는다.
        // height는 대상 위 높이다. 표지판 같은 물체는 원작 DialogueBubbleProxy의 yOffset을 넘기고, 생략하면 플레이어 높이다.
        internal void Bubble(string value, Transform target, Camera camera, float sustain, float height = _playerBubbleHeight)
        {
            _bubbleText.text = value; _bubbleTarget = target; _worldCamera = camera; _bubbleHeight = height;
            _bubbleLetters = 0; _bubbleText.maxVisibleCharacters = 0;
            _bubbleUntil = Time.unscaledTime + Mathf.Max(_bubbleMinimumSustain, sustain) + value.Length / _bubbleLettersPerSecond;
            _bubblePanel.SetActive(true);
        }
        // 원작 Health.Hurt의 PopText처럼 최종 피해를 소수 한 자리로 피해 지점에서 띄운다. 원작 물리 피해 모양대로 색은 하나이고,
        // 치명이면 크기를 키우고 치명 아이콘을 붙인다. 모양 값은 원작 UIStyleData 자리인 게임플레이 UI 스타일 데이터다.
        internal void Damage(Vector3 point, float value, bool crit, Camera camera)
        {
            GameplayDataSettings.UIStyleData style = GameplayDataSettings.UIStyle;
            Pop(value.ToString("F1"), style.DamagePopColor, crit ? style.DamagePopCritSize : 1f, crit ? style.CritPopSprite : null, point, camera);
        }
        // 원본 CharacterMainControl의 PopText(예: Poptext_OutOfAmmo)처럼 캐릭터 위치에서 문구를 띄운다. 원작처럼 크기 1에 아이콘이 없다.
        internal void PopText(string text, Vector3 point, Camera camera) => Pop(text, Color.white, 1f, null, point, camera);
        // 피해 숫자와 캐릭터 문구가 같은 템플릿 풀을 쓰고, 원본처럼 받은 지점보다 조금 위에서 떠오른다.
        // 개수가 계속 바뀌므로 숨긴 문구를 재사용하고 모자랄 때만 복제한다. 크기와 아이콘은 원작 PopText.InstancePop처럼 띄울 때마다 정한다.
        void Pop(string text, Color color, float size, Sprite? sprite, Vector3 point, Camera camera)
        {
            _worldCamera = camera;
            (TMP_Text number, Image? icon) = _numberPool.Count > 0 ? _numberPool.Pop() : CloneTemplate();
            number.text = text; number.color = color;
            // 크기는 템플릿에 저작한 배율에 곱해, 재사용한 문구에 앞 크기가 남지 않게 매번 덮어쓴다.
            number.transform.localScale = _damageTemplate.transform.localScale * size;
            // 원작 PopTextEntity.SetupContent처럼 스프라이트가 있을 때만 아이콘을 켠다. 템플릿에 아이콘이 없으면 숫자만 띄운다.
            if (icon != null)
            {
                bool showIcon = sprite != null;
                if (showIcon) icon.sprite = sprite;
                icon.gameObject.SetActive(showIcon);
                icon.canvasRenderer.SetAlpha(1f);
            }
            // 사라지는 알파는 CanvasRenderer에 둔다. TMP color를 바꾸면 매 프레임 메시를 다시 만들기 때문이다. 재사용한 숫자는 켠 뒤 불투명으로 되돌린다.
            number.gameObject.SetActive(true); number.canvasRenderer.SetAlpha(1f); _numbers.Add((number, icon, point + Vector3.up * _popRise, Time.unscaledTime));
        }
        // 풀이 비었을 때만 부른다. 치명 아이콘은 템플릿의 자식 Image라 복제본 안에서 한 번만 찾고, 없으면 null이다.
        (TMP_Text text, Image? icon) CloneTemplate()
        {
            TMP_Text number = Instantiate(_damageTemplate, _damageTemplate.transform.parent);
            return (number, number.GetComponentInChildren<Image>(true));
        }
        // 피격 표시는 마지막 피격부터 정해진 시간 동안 사라진다.
        internal void PlayerHit() => _hitUntil = Time.unscaledTime + _hitFlashDuration;
        // 말풍선, 피격 표시, 피해 숫자만 갱신한다. 대사 상자는 DialogueBoxView가, 게이지와 적 체력바는 공용 HUD가 갱신한다.
        void LateUpdate()
        {
            // 프레임 안에서 시각이 같으므로 한 번만 읽어 함께 쓴다.
            float now = Time.unscaledTime;
            if (_bubblePanel.activeSelf)
            {
                if (_bubbleTarget == null || _worldCamera == null || now >= _bubbleUntil) _bubblePanel.SetActive(false);
                else
                {
                    _bubbleLetters += Time.unscaledDeltaTime * _bubbleLettersPerSecond; _bubbleText.maxVisibleCharacters = (int)_bubbleLetters;
                    // 띄울 때 받은 높이를 쓰고, 화면에서 화면 높이 비율만큼 더 올린다.
                    _bubblePanel.transform.position = _worldCamera.WorldToScreenPoint(_bubbleTarget.position + Vector3.up * _bubbleHeight) +
                        Vector3.up * (Screen.height * _bubbleScreenLift);
                }
            }
            // 남은 피격 시간에 비례한 알파로 사라진다.
            _hitFlash.color = new Color(_hitFlashRed, 0, 0, Mathf.Max(0, _hitUntil - now) * _hitFlashAlphaScale);
            // 반복 중에는 바뀌지 않으므로 카메라 파괴 검사를 반복 앞에서 한 번만 한다.
            bool noCamera = _worldCamera == null;
            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                var n = _numbers[i]; float elapsed = now - n.start;
                // 숨긴 숫자의 문구는 비우지 않는다. 다시 꺼낼 때 Pop이 켜기 전에 덮어쓰므로 TMP 메시를 한 번 덜 만든다.
                if (elapsed >= _popLifetime || noCamera)
                { n.text.gameObject.SetActive(false); _numberPool.Push((n.text, n.icon)); _numbers.RemoveAt(i); continue; }
                n.text.transform.position = _worldCamera.WorldToScreenPoint(n.point + Vector3.up * elapsed);
                n.text.canvasRenderer.SetAlpha(1 - elapsed);
                // CanvasRenderer 알파는 자식에게 이어지지 않으므로 아이콘도 같은 알파로 따로 흐리게 한다.
                if (n.icon != null) n.icon.canvasRenderer.SetAlpha(1 - elapsed);
            }
        }
    }
}
