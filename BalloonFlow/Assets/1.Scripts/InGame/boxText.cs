using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera _mainCam;

    void Update()
    {
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam != null) transform.forward = _mainCam.transform.forward;
    }
}