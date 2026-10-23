#nullable enable annotations
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace Game.App
{
    // 상태나 선택지처럼 실행 중 바뀌는 TMP 문구의 번역 구독을 화면 수명 동안 보관한다.
    // 고정 제목과 버튼은 씬에 직렬화된 LocalizedUguiText가 직접 구독한다.
    public sealed class LocalizedTextBindings : IDisposable
    {
        readonly Dictionary<string, Binding> _bindings = new Dictionary<string, Binding>();
        bool _disposed;

        // 번역된 문자열이 아니라 GameUI 테이블 키로 같은 구독인지 판별한다. 키가 같으면 Refresh만 하고,
        // 키가 바뀔 때만 LocalizedString을 새로 만들어 갱신마다 할당하지 않는다.
        public void Set(string id, string key, Action<string> apply)
        {
            if (_disposed) return;
            if (_bindings.TryGetValue(id, out Binding current))
            {
                if (current.Matches(key))
                {
                    current.Refresh();
                    return;
                }
                current.Dispose();
            }

            var binding = new Binding(new LocalizedString(GameLocalization.Table, key), apply);
            _bindings[id] = binding;
            binding.Connect();
        }

        // 드롭다운 선택지를 만드는 유일한 자리다. 옵션 화면의 언어와 해상도 목록, BindChoices가 쓴다.
        // 선택지를 교체해도 값 변경 이벤트는 일어나지 않는다.
        internal static void SetChoices(TMP_Dropdown dropdown, List<string> labels)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.RefreshShownValue();
        }

        // 키 순서대로 선택지를 만들고 현재 선택 인덱스를 지킨다. 번역이 오기 전에는 키가 보이고, 도착하면 각 선택지 글자만 바뀐다.
        public void BindChoices(string id, TMP_Dropdown dropdown, IReadOnlyList<string> keys)
        {
            int selection = dropdown.value;
            SetChoices(dropdown, new List<string>(keys));
            dropdown.SetValueWithoutNotify(selection);
            for (int i = 0; i < keys.Count; i++)
            {
                int index = i;
                Set(id + "." + i, keys[i], value =>
                {
                    if ((uint)index >= (uint)dropdown.options.Count) return;
                    int selected = dropdown.value;
                    dropdown.options[index].text = value;
                    dropdown.SetValueWithoutNotify(selected);
                    dropdown.RefreshShownValue();
                });
            }
        }

        // 빈 문구는 번역을 요청하지 않는다. 구독을 해제하므로 늦게 도착한 번역이 빈 문구를 덮어쓰지 않는다.
        public void Clear(string id, Action<string> apply)
        {
            if (_bindings.TryGetValue(id, out Binding binding))
            {
                binding.Dispose();
                _bindings.Remove(id);
            }
            apply("");
        }

        // 여러 번 불러도 안전하다. 해제 뒤에는 닫힌 화면으로 번역 콜백이 가지 않는다.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Binding binding in _bindings.Values) binding.Dispose();
            _bindings.Clear();
        }

        // 등록한 대리자를 보관해 같은 대리자로 구독을 해제한다.
        sealed class Binding : IDisposable
        {
            readonly LocalizedString _reference;
            readonly LocalizedString.ChangeHandler _handler;
            string? _value;
            bool _disposed;

            public Binding(LocalizedString reference, Action<string> apply)
            {
                _reference = reference;
                _handler = value =>
                {
                    if (_disposed) return;
                    _value = value;
                    apply(value);
                };
            }

            // 모든 구독이 GameUI 테이블을 쓰므로 키만 비교한다.
            // 문자열 키를 LocalizedString 생성자와 같은 암시 변환으로 TableEntryReference로 만들어 비교한다.
            public bool Matches(string key) => _reference.TableEntryReference.Equals((TableEntryReference)key);

            public void Connect() => _reference.StringChanged += _handler;
            public void Refresh() { if (_value != null) _handler(_value); }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _reference.StringChanged -= _handler;
            }
        }
    }
}
