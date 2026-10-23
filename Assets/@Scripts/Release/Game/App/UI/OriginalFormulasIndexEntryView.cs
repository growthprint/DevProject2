#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.FormulasIndexEntry(레시피 도감 칸 하나)다. 해금한 제조식은 결과 아이템 아이콘과 "이름 x수량"을 보이고,
    // 해금하지 않은 제조식은 원작 lockedImage와 lockedText로, 결과 ID가 음수인 제조식은 "! 제조식 ID !"와 lockedImage로 보인다.
    // 클릭은 도감 창(OriginalFormulasIndexView)에 넘긴다. 원작처럼 고른 칸을 따로 표시하지 않는다.
    // 원작 클릭 흔들림(PunchReceiver)과 호버 표시 컴포넌트는 옮기지 않았다(열쇠 도감 칸 선례).
    public sealed class OriginalFormulasIndexEntryView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] Image _image = null!;
        // 원작 lockedText와 lockedImage다. 제작 도구가 원본 값을 옮긴다.
        [SerializeField] string _lockedText = "???";
        [SerializeField] Sprite _lockedImage = null!;

        Action<string>? _clicked;
        string _formulaId = "";

        // 원작 Refresh다. valid는 원작 Valid(결과 ID 0 이상), unlocked는 제조 소유자의 해금 여부다.
        // 해금하지 않은 칸은 이름과 아이콘을 쓰지 않으므로 컨트롤러가 빈 값을 넘겨도 된다.
        internal void Bind(Action<string> clicked, string formulaId, bool valid, bool unlocked, string resultName, int amount, Sprite? icon)
        {
            _clicked = clicked;
            _formulaId = formulaId;
            if (!valid)
            {
                _name.text = "! " + formulaId + " !";
                _image.sprite = _lockedImage;
                return;
            }
            if (unlocked)
            {
                _name.text = resultName + " x" + amount;
                _image.sprite = icon;
                return;
            }
            _name.text = _lockedText;
            _image.sprite = _lockedImage;
        }

        // 목록을 다시 채우거나 창을 닫을 때 앞 클릭 대상을 끊는다.
        internal void ResetBinding() => _clicked = null;

        public void OnPointerClick(PointerEventData eventData) => _clicked?.Invoke(_formulaId);
    }
}
