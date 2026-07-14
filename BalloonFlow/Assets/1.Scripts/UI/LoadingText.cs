using System.Collections;
using TMPro;
using UnityEngine;

public class LoadingDots : MonoBehaviour
{
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private float interval = 0.4f;

    private void OnEnable()
    {
        StartCoroutine(AnimateDots());
    }

    private IEnumerator AnimateDots()
    {
        // ROLLBACK_LOADING_LOCALIZE_20260714: 단어만 지역화(EN "LOADING"/KO "로딩"), 점 애니메이션은 유지.
        //   언어는 LocalizationService.AutoSelectDeviceLanguage(BeforeSceneLoad)가 씬보다 먼저 세팅됨.
        string word = BalloonFlow.LocalizationService.Get("loading.dots");
        string[] states = { word, word + ".", word + ".." };
        int index = 0;

        while (true)
        {
            loadingText.text = states[index];
            index = (index + 1) % states.Length;
            yield return new WaitForSeconds(interval);
        }
    }
}