using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 프로젝트 표준 버튼. Unity Button을 확장하며 ButtonScaleEffect가 자동 부착된다.
    /// 아트팀은 UIButton 프리팹을 드래그해 사용하거나, 일반 GameObject에 이 컴포넌트를 추가하면 된다.
    /// </summary>
    [RequireComponent(typeof(ButtonScaleEffect))]
    [AddComponentMenu("BalloonFlow/UI/UIButton")]
    public class UIButton : Button
    {
    }
}
