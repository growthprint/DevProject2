#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 LevelManager/DialogueSystem에 있는 Dialogues.DialogueUI의 표시 규칙을 재구성한 대사 상자다.
    // Space와 클릭 입력 해석, 대사 진행, 열림 판정은 씬 소유자의 DialogueSequenceRunner가 맡는다.
    public sealed class DialogueBoxView : MonoBehaviour
    {
        [SerializeField] CanvasGroup _mainGroup = null!;
        [SerializeField] CanvasGroup _textGroup = null!;
        [SerializeField] TMP_Text _text = null!;
        [SerializeField] GameObject _continueIndicator = null!;
        [SerializeField] RectTransform _continueImage = null!;
        [SerializeField] CanvasGroup _nameGroup = null!;
        [SerializeField] TMP_Text _nameText = null!;
        [SerializeField] GameObject _portraitContainer = null!;
        [SerializeField] Image _portrait = null!;
        // 원본 DialogueUI.speed(프리팹 값 40)와 FadeGroup 0.2초, ▼ 반동(0.5초, 0 → -16)을 그대로 쓴다.
        // 두 시간은 나누는 값이라 다른 페이드 View와 같은 UiFade 최소 시간 아래로 내리지 않는다.
        [SerializeField, Min(1f)] float _charactersPerSecond = 40f;
        [SerializeField, Min(UiFade._minimumDuration)] float _fadeDuration = .2f;
        [SerializeField] Vector2 _indicatorFrom = Vector2.zero;
        [SerializeField] Vector2 _indicatorTo = new(0f, -16f);
        [SerializeField, Min(UiFade._minimumDuration)] float _indicatorDuration = .5f;

        int _visible;
        int _total;
        float _buffer;
        float _indicatorTime;
        // 본문을 배치하는 부모 레이아웃 그룹이다. 줄마다 GetComponentInParent를 부르지 않도록 처음 찾은 값을 둔다.
        LayoutGroup? _layout;

        // 원본은 첫 글자에 UI/dialogue_start, 이후 글자마다 UI/dialogue_bump를 재생한다(true = 첫 글자).
        public event Action<bool>? CharacterRevealed;

        // 새 줄을 처음부터 드러내며, 화자가 없으면 이름 탭과 초상화를 숨긴다.
        // 열림은 자기 루트의 활성 상태다. 루트는 ShowLine과 Hide만 켜고 끈다.
        public void ShowLine(string text, string? speaker, Sprite? portrait = null)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                _mainGroup.alpha = 0f;
            }
            _text.text = text;
            // 처음 열 때는 ContentArea 레이아웃이 본문 영역 크기를 아직 정하지 않아, 직렬화 값 0×0 때문에 글자 수가 0으로 측정된다.
            // 측정 전에 레이아웃을 즉시 갱신해 첫 줄도 글자를 차례로 드러내고 확인 규칙을 따르게 한다.
            // 레이아웃 그룹은 UnityEngine.Object라 ??= 대신 == null로 비교한다.
            if (_layout == null) _layout = _text.GetComponentInParent<LayoutGroup>();
            if (_layout != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_layout.transform);
            _text.ForceMeshUpdate();
            _total = _text.textInfo.characterCount;
            _visible = 0;
            _buffer = 0f;
            _text.maxVisibleCharacters = 0;
            _textGroup.alpha = 0f;
            _continueIndicator.SetActive(false);
            bool named = !string.IsNullOrEmpty(speaker);
            _nameText.text = named ? speaker : "";
            _nameGroup.alpha = named ? 1f : 0f;
            _nameGroup.gameObject.SetActive(named);
            _portraitContainer.SetActive(portrait != null);
            _portrait.sprite = portrait;
        }

        // 표시 중이면 남은 글자를 모두 드러내고 false를, 다 보인 뒤면 다음 줄로 넘기라고 true를 돌려준다.
        // 원본 DoSubtitle처럼 한 번에 드러낸 글자도 같은 프레임에 글자마다 공개 이벤트를 내서 표시음이 난다.
        public bool Confirm()
        {
            if (_visible < _total)
            {
                while (_visible < _total) RevealNext();
                _text.maxVisibleCharacters = _total;
                return false;
            }
            return true;
        }

        // TMP 메시를 다시 만들지 않도록 숨길 때 본문을 비우지 않는다. 꺼진 상자는 보이지 않고, 다시 열면 ShowLine이 렌더 전에 덮어쓴다.
        public void Hide()
        {
            _continueIndicator.SetActive(false);
            gameObject.SetActive(false);
        }

        // 루트가 켜진 동안만 돌고 루트가 켜져 있으면 열린 상자이므로 열림을 다시 보지 않는다.
        void Update()
        {
            float delta = Time.unscaledDeltaTime;
            // 원본 FadeGroup처럼 상자와 본문을 다른 페이드 View와 같은 UiFade 규칙으로 나타나게 한다.
            _mainGroup.alpha = UiFade.Step(_mainGroup.alpha, 1f, delta, _fadeDuration);
            _textGroup.alpha = UiFade.Step(_textGroup.alpha, 1f, delta, _fadeDuration);
            if (_visible < _total)
            {
                _buffer += delta * _charactersPerSecond;
                while (_buffer >= 1f && _visible < _total)
                {
                    _buffer -= 1f;
                    RevealNext();
                }
                _text.maxVisibleCharacters = _visible;
            }
            bool finished = _visible >= _total;
            // 매 프레임 불리므로 ▼ 표시는 상태가 바뀔 때만 SetActive를 부른다.
            UiVisibility.Set(_continueIndicator, finished);
            if (!finished) return;
            _indicatorTime += delta;
            _continueImage.anchoredPosition = Vector2.Lerp(_indicatorFrom, _indicatorTo, Mathf.PingPong(_indicatorTime / _indicatorDuration, 1f));
        }

        // 원본처럼 글자 하나를 드러낼 때마다 공개 이벤트를 낸다. 확인으로 한 번에 드러낸 글자와 시간에 따라 드러낸 글자가 같은 규칙을 쓴다.
        void RevealNext()
        {
            _visible++;
            CharacterRevealed?.Invoke(_visible == 1);
        }
    }
}
