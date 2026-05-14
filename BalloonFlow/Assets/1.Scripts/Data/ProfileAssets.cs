using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// [2026-05-13] 프로필 아이콘 / 프레임 sprite 카탈로그. ScriptableObject 1개로 owner 통합.
    /// UILobby (좌측 상단 표시) 와 PopupProfile (선택 UI) 둘 다 이걸 참조 → 중복 sprite 배열 회피.
    ///
    /// 사용법:
    ///  1. Project 창에서 우클릭 → Create → BalloonFlow → Profile Assets.
    ///  2. 만들어진 .asset 의 icons / frames 배열에 슬롯 sprite 채움.
    ///  3. UILobby._profileAssets 와 PopupProfile._profileAssets 에 같은 .asset 드래그.
    ///
    /// UserData.profileIconNumber / profileFrameNumber 는 배열 index. 0 = 첫 슬롯.
    /// 잘못된 index 또는 배열 미설정 시 GetIcon/GetFrame 은 null 반환 → caller 가 fallback.
    /// </summary>
    [CreateAssetMenu(fileName = "ProfileAssets", menuName = "BalloonFlow/Profile Assets", order = 410)]
    public class ProfileAssets : ScriptableObject
    {
        [Tooltip("프로필 아이콘 sprite 슬롯. UserData.profileIconNumber index 와 일치.")]
        [SerializeField] private Sprite[] _icons;

        [Tooltip("프로필 프레임 sprite 슬롯. UserData.profileFrameNumber index 와 일치.")]
        [SerializeField] private Sprite[] _frames;

        public int IconCount => _icons != null ? _icons.Length : 0;
        public int FrameCount => _frames != null ? _frames.Length : 0;

        public Sprite GetIcon(int index)
        {
            if (_icons == null || index < 0 || index >= _icons.Length) return null;
            return _icons[index];
        }

        public Sprite GetFrame(int index)
        {
            if (_frames == null || index < 0 || index >= _frames.Length) return null;
            return _frames[index];
        }
    }
}
