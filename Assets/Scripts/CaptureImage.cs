using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class CaptureImage : MonoBehaviour
{
    public const int RenderTextureDepth = 24;

    [HideInInspector] public UnityEvent<Texture2D> PhotoCaptured = new UnityEvent<Texture2D>();

    [SerializeField] private Button takePhotoButton;
    [SerializeField] private Transform photosContent;
    [SerializeField] private GameObject photoPrefab;
    
    private Camera mainCamera;

    private void Awake()
    {
        mainCamera = Camera.main;
        takePhotoButton.interactable = false;
    }

    private void OnEnable()
    {
        takePhotoButton.onClick.AddListener(TakePhoto);
    }

    private void OnDisable()
    {
        takePhotoButton.onClick.RemoveListener(TakePhoto);
    }

    private void Start()
    {
        if (Application.platform != RuntimePlatform.WindowsEditor)
        {
            StartCoroutine(WaitForStartTracking());
        }
    }

    private IEnumerator WaitForStartTracking()
    {
        yield return new WaitUntil(() => ARSession.state == ARSessionState.SessionTracking);
        takePhotoButton.interactable = true;
    }

    private void TakePhoto()
    {
        if (takePhotoButton.interactable)
        {
            StartCoroutine(TakePhotoCoroutine());
        }
    }

    private IEnumerator TakePhotoCoroutine()
    {
        takePhotoButton.interactable = false;
        yield return StartCoroutine(CaptureImageCoroutine());
        takePhotoButton.interactable = true;
    }

    private IEnumerator CaptureImageCoroutine()
    {
        yield return new WaitForEndOfFrame();
        var screenshot = GetScreenshotWithoutUI();
        var photoUI = Instantiate(photoPrefab, photosContent);
        photoUI.GetComponent<SetImage>().SetPhoto(screenshot);
        PhotoCaptured.Invoke(screenshot);
    }

    private Texture2D GetScreenshotWithoutUI()
    {
        var currentRenderTexture = RenderTexture.active;
        var renderTexture = new RenderTexture(Screen.width, Screen.height, RenderTextureDepth);
        mainCamera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        mainCamera.Render();

        var image = new Texture2D(mainCamera.targetTexture.width, mainCamera.targetTexture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, mainCamera.targetTexture.width, mainCamera.targetTexture.height), 0, 0);
        image.Apply();
        image.name = Guid.NewGuid().ToString();

        CleanupForPhotoCapturing(currentRenderTexture, renderTexture);
        return image;
    }

    private void CleanupForPhotoCapturing(RenderTexture currentRenderTexture, RenderTexture renderTexture)
    {
        mainCamera.targetTexture = null;
        RenderTexture.active = currentRenderTexture;
        Destroy(renderTexture);
        Destroy(currentRenderTexture);
    }
}
