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
        string[] states = { "LOADING", "LOADING.", "LOADING.." };
        int index = 0;

        while (true)
        {
            loadingText.text = states[index];
            index = (index + 1) % states.Length;
            yield return new WaitForSeconds(interval);
        }
    }
}