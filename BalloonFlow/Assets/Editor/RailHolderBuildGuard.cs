using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// PROTO_RAIL_HOLDER_20260716 — 릴리즈 혼입 방지 가드.
    ///
    /// BF_RAIL_HOLDER 는 "홀더가 레일을 탄다" 프로토타입을 컴파일에 포함시키는 define 이다.
    /// Unity 의 Scripting Define Symbols 는 <b>플랫폼 단위</b>라 빌드 구성(Development 여부)과 무관하게 적용된다.
    /// → 에디터에서 켜놓고 잊은 채 출시 AAB 를 뽑으면 프로토 코드가 그대로 실려나간다.
    ///
    /// 그래서 조용히 무시하지 않고 <b>빌드를 실패</b>시킨다. 출시 빌드(비-development)에서 define 이 켜져 있으면
    /// BuildFailedException 으로 중단 → 혼입이 구조적으로 불가능.
    ///
    /// 프로토를 실기에서 보고 싶으면 Development Build 를 켜면 된다(그건 통과시킨다).
    /// </summary>
    public class RailHolderBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000; // 다른 전처리보다 먼저 — 실패시킬 거면 빨리 실패

        private const string DEFINE = "BF_RAIL_HOLDER";

        public void OnPreprocessBuild(BuildReport report)
        {
#if BF_RAIL_HOLDER
            bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            if (isDevelopmentBuild)
            {
                Debug.LogWarning(
                    $"[RailHolderBuildGuard] {DEFINE} 이(가) 켜진 채 Development Build 를 진행합니다. " +
                    "프로토(레일 홀더 모드) 코드가 이 빌드에 포함됩니다. 출시 빌드에서는 자동 차단됩니다.");
                return;
            }

            NamedBuildTarget namedTarget = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
            throw new BuildFailedException(
                $"[RailHolderBuildGuard] 출시 빌드 차단 — 프로토 define '{DEFINE}' 이(가) " +
                $"{namedTarget.TargetName} 플랫폼에 켜져 있습니다.\n" +
                $"이 define 은 '홀더가 레일을 탄다' 프로토타입 전용이며 출시 빌드에 포함되면 안 됩니다.\n" +
                $"조치: Project Settings > Player > Other Settings > Scripting Define Symbols 에서 " +
                $"'{DEFINE}' 를 제거한 뒤 다시 빌드하세요.\n" +
                $"(실기에서 프로토를 확인하려는 것이라면 Build Settings 에서 Development Build 를 켜세요.)");
#else
            // define 이 꺼져 있으면 프로토 코드는 애초에 컴파일되지 않음 — 검사할 것 없음.
            return;
#endif
        }
    }
}
